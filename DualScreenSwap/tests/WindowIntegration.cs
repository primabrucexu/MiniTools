using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using DualScreenSwap;

internal static class WindowIntegration
{
    private static Process fixture;
    private static readonly Dictionary<string, IntPtr> Handles = new Dictionary<string, IntPtr>();
    private static Screen[] Screens;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "fixture") { RunFixture(); return 0; }
        try
        {
            Screens = Screen.AllScreens;
            Array.Sort(Screens, delegate(Screen a, Screen b) { return a.Bounds.Left.CompareTo(b.Bounds.Left); });
            Check(Screens.Length == 2, "requires exactly two monitors");
            Console.WriteLine("MONITORS " + Screens[0].Bounds + " / " + Screens[1].Bounds);
            ProcessStartInfo start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "fixture");
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            fixture = Process.Start(start);
            string ready = ReadReply();
            Check(ready.StartsWith("READY|"), "fixture ready: " + ready);
            foreach (string pair in ready.Substring(6).Split(','))
            {
                string[] pieces = pair.Split('=');
                Handles.Add(pieces[0], new IntPtr(Int64.Parse(pieces[1])));
            }
            TestNormalRoundTrip();
            TestMixed();
            TestBothMaximized();
            Console.WriteLine("PASS ALL 3 integration scenarios; ordinary non-topmost windows, 4 actual swaps.");
            Console.WriteLine("NOT TESTED missing-target scenario: existing user applications remain on both screens.");
            Console.WriteLine("NOT TESTED unequal DPI/resolution: current monitors share the tested geometry.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL " + error);
            return 1;
        }
        finally
        {
            if (fixture != null && !fixture.HasExited)
            {
                try { fixture.StandardInput.WriteLine("QUIT"); fixture.StandardInput.Flush(); } catch { }
                if (!fixture.WaitForExit(3000)) fixture.Kill();
            }
        }
    }

    private static void TestNormalRoundTrip()
    {
        Command("RESET");
        Dictionary<string, Rect> original = Capture();
        Check((Api.GetWindowLong(Handles["AF"], -20) & 8) == 0 &&
            (Api.GetWindowLong(Handles["BF"], -20) & 8) == 0, "fixtures are ordinary non-topmost windows");
        SafeSwap("AF", "BF");
        AssertRect("AF", Translate(original["AF"], 0, 1));
        AssertRect("BF", Translate(original["BF"], 1, 0));
        Check(!Api.IsZoomed(Handles["AF"]) && !Api.IsZoomed(Handles["BF"]), "normal states retained");
        AssertBacks(original);
        AssertCandidates("BF", "AF");
        SafeSwap("BF", "AF");
        foreach (string key in Handles.Keys) AssertRect(key, original[key]);
        AssertCandidates("AF", "BF");
        Console.WriteLine("PASS normal round-trip: exact dimensions/offsets, forefront selection, backgrounds unchanged.");
    }

    private static void TestMixed()
    {
        Command("RESET");
        Dictionary<string, Rect> normal = Capture();
        Command("MAX AF");
        SafeSwap("AF", "BF");
        AssertMaximized("AF", 1);
        Check(!Api.IsZoomed(Handles["BF"]), "mixed normal state retained");
        AssertRect("BF", Translate(normal["BF"], 1, 0));
        AssertBacks(normal);
        AssertCandidates("BF", "AF");
        Command("RESTORE AF");
        AssertRect("AF", Translate(normal["AF"], 0, 1));
        Console.WriteLine("PASS mixed states: maximized window remains maximized; Restore stays on destination screen.");
    }

    private static void TestBothMaximized()
    {
        Command("RESET");
        Dictionary<string, Rect> normal = Capture();
        Command("MAX AF");
        Command("MAX BF");
        SafeSwap("AF", "BF");
        AssertMaximized("AF", 1);
        AssertMaximized("BF", 0);
        AssertBacks(normal);
        AssertCandidates("BF", "AF");
        Command("RESTORE AF");
        Command("RESTORE BF");
        AssertRect("AF", Translate(normal["AF"], 0, 1));
        AssertRect("BF", Translate(normal["BF"], 1, 0));
        Console.WriteLine("PASS both maximized: target monitor/state and both restored bounds are correct.");
    }

    private static void SafeSwap(string left, string right)
    {
        AssertCandidates(left, right);
        // No user windows are moved unless the same product filter just selected only fixture HWNDs.
        string error = WindowSwapper.Swap();
        Check(error == null, "swap result: " + error);
        Thread.Sleep(250);
    }

    private static void AssertCandidates(string left, string right)
    {
        MethodInfo filter = typeof(WindowSwapper).GetMethod("IsApplicationWindow", BindingFlags.NonPublic | BindingFlags.Static);
        IntPtr[] candidates = new IntPtr[2];
        Api.EnumWindows(delegate(IntPtr window, IntPtr data)
        {
            if (!(bool)filter.Invoke(null, new object[] { window })) return true;
            int index = Screen.FromHandle(window).DeviceName == Screens[0].DeviceName ? 0 : 1;
            if (candidates[index] == IntPtr.Zero) candidates[index] = window;
            return candidates[0] == IntPtr.Zero || candidates[1] == IntPtr.Zero;
        }, IntPtr.Zero);
        if (candidates[0] != Handles[left] || candidates[1] != Handles[right])
        {
            foreach (KeyValuePair<string, IntPtr> item in Handles)
                Console.WriteLine("DIAGNOSTIC fixture " + item.Key + " hwnd=" + item.Value + " eligible=" +
                    filter.Invoke(null, new object[] { item.Value }) + " style=" + Api.GetWindowLong(item.Value, -16).ToString("X") +
                    " exstyle=" + Api.GetWindowLong(item.Value, -20).ToString("X") + " rect=" + GetRect(item.Key));
            foreach (IntPtr candidate in candidates)
                Console.WriteLine("DIAGNOSTIC candidate " + candidate + " exstyle=" + Api.GetWindowLong(candidate, -20).ToString("X"));
        }
        Check(candidates[0] == Handles[left] && candidates[1] == Handles[right],
            "SAFETY: first eligible windows must be expected fixture windows; actual " + candidates[0] + "/" + candidates[1]);
        foreach (IntPtr window in candidates)
        {
            uint pid;
            Api.GetWindowThreadProcessId(window, out pid);
            Check(pid == fixture.Id, "SAFETY: candidate process must equal fixture process");
        }
    }

    private static Dictionary<string, Rect> Capture()
    {
        Dictionary<string, Rect> result = new Dictionary<string, Rect>();
        foreach (string key in Handles.Keys) result.Add(key, GetRect(key));
        return result;
    }

    private static Rect GetRect(string key)
    {
        Rect result;
        Check(Api.GetWindowRect(Handles[key], out result), "read rectangle " + key);
        return result;
    }

    private static Rect Translate(Rect rect, int from, int to)
    {
        int x = Screens[to].Bounds.Left - Screens[from].Bounds.Left;
        int y = Screens[to].Bounds.Top - Screens[from].Bounds.Top;
        return new Rect { Left = rect.Left + x, Right = rect.Right + x, Top = rect.Top + y, Bottom = rect.Bottom + y };
    }

    private static void AssertRect(string key, Rect expected)
    {
        Rect actual = GetRect(key);
        Check(actual.Equals(expected), key + " expected " + expected + ", actual " + actual);
    }

    private static void AssertBacks(Dictionary<string, Rect> original)
    {
        AssertRect("AB", original["AB"]);
        AssertRect("BB", original["BB"]);
    }

    private static void AssertMaximized(string key, int monitor)
    {
        Check(Api.IsZoomed(Handles[key]), key + " must remain maximized");
        Check(Screen.FromHandle(Handles[key]).DeviceName == Screens[monitor].DeviceName, key + " maximized on wrong monitor");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Command(string command)
    {
        fixture.StandardInput.WriteLine(command);
        fixture.StandardInput.Flush();
        Check(ReadReply() == "OK", "fixture command " + command);
        Thread.Sleep(200);
    }

    private static string ReadReply()
    {
        System.Threading.Tasks.Task<string> read = fixture.StandardOutput.ReadLineAsync();
        Check(read.Wait(5000), "fixture response timeout");
        return read.Result ?? "EOF";
    }

    private static void RunFixture()
    {
        Application.EnableVisualStyles();
        Screen[] screens = Screen.AllScreens;
        Array.Sort(screens, delegate(Screen a, Screen b) { return a.Bounds.Left.CompareTo(b.Bounds.Left); });
        Dictionary<string, Form> forms = new Dictionary<string, Form>();
        Dictionary<string, Rectangle> original = new Dictionary<string, Rectangle>();
        string[] keys = { "AB", "BB", "AF", "BF" };
        original.Add("AB", new Rectangle(screens[0].Bounds.Left + 90, screens[0].Bounds.Top + 110, 480, 350));
        original.Add("BB", new Rectangle(screens[1].Bounds.Left + 80, screens[1].Bounds.Top + 130, 530, 380));
        original.Add("AF", new Rectangle(screens[0].Bounds.Left + 160, screens[0].Bounds.Top + 180, 700, 520));
        original.Add("BF", new Rectangle(screens[1].Bounds.Left + 210, screens[1].Bounds.Top + 230, 650, 450));
        foreach (string key in keys)
        {
            Form form = new Form();
            form.Text = "DualScreenSwap TEST ONLY - " + key;
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = original[key];
            form.BackColor = key.EndsWith("F") ? Color.LightBlue : Color.LightGray;
            form.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Text = "DualScreenSwap integration test\r\n" + key + "\r\nThese test windows will close automatically.",
                Font = new Font("Segoe UI", 15) });
            forms.Add(key, form);
            form.Show();
        }
        Form dispatcher = forms["AF"];
        Thread commands = new Thread(delegate()
        {
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                string command = line;
                dispatcher.Invoke((Action)delegate()
                {
                    if (command == "QUIT") { Application.ExitThread(); return; }
                    if (command == "RESET")
                    {
                        foreach (string key in keys)
                        {
                            forms[key].WindowState = FormWindowState.Normal;
                            forms[key].Bounds = original[key];
                            forms[key].Show();
                            // A background fixture may not be promoted by HWND_TOP alone.
                            // Return it immediately to the ordinary window group before testing.
                            forms[key].TopMost = true;
                            forms[key].TopMost = false;
                        }
                    }
                    else if (command.StartsWith("MAX ")) forms[command.Substring(4)].WindowState = FormWindowState.Maximized;
                    else if (command.StartsWith("RESTORE ")) forms[command.Substring(8)].WindowState = FormWindowState.Normal;
                    Console.WriteLine("OK");
                    Console.Out.Flush();
                });
                if (line == "QUIT") break;
            }
        });
        commands.IsBackground = true;
        dispatcher.Shown += delegate { };
        System.Windows.Forms.Timer ready = new System.Windows.Forms.Timer { Interval = 100 };
        ready.Tick += delegate
        {
            ready.Stop();
            List<string> values = new List<string>();
            foreach (string key in keys) values.Add(key + "=" + forms[key].Handle.ToInt64());
            Console.WriteLine("READY|" + String.Join(",", values.ToArray()));
            Console.Out.Flush();
            commands.Start();
        };
        ready.Start();
        Application.Run();
        foreach (Form form in forms.Values) form.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left, Top, Right, Bottom;
        public override string ToString() { return String.Format("({0},{1}) {2}x{3}", Left, Top, Right - Left, Bottom - Top); }
    }

    private static class Api
    {
        internal delegate bool EnumCallback(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumCallback callback, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    }
}
