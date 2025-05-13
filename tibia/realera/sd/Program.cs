using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    [DllImport("kernel32.dll")]
    static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] buffer,
        int size,
        out int lpNumberOfBytesRead
    );

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    static extern bool SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    const int PROCESS_ALL_ACCESS = 0x001F0FFF;
    const uint WM_KEYDOWN = 0x0100;
    const uint WM_KEYUP = 0x0101;
    const uint WM_LBUTTONDOWN = 0x0201;
    const uint WM_LBUTTONUP = 0x0202;
    const uint WM_MOUSEMOVE = 0x0200;
    const int VK_F1 = 0x70;
    const int VK_F2 = 0x71;
    const int VK_F3 = 0x72;

    static IntPtr targetWindow = IntPtr.Zero;
    static IntPtr processHandle = IntPtr.Zero;
    static IntPtr moduleBase = IntPtr.Zero;

    // Thresholds
    static int HP_THRESHOLD = 70;
    static int MANA_THRESHOLD = 1000;
    static double SOUL_THRESHOLD = 5.0;

    // Memory addresses
    static IntPtr BASE_ADDRESS = 0x009432D0;
    static int HP_OFFSET = 1184;
    static int MAX_HP_OFFSET = 1192;
    static int MANA_OFFSET = 1240;
    static int MAX_MANA_OFFSET = 1248;
    static int SOUL_OFFSET = 1280;

    static double curHP = 0, maxHP = 1;
    static double curMana = 0, maxMana = 1;
    static double curSoul = 0;
    static string processName = "RealeraDX";

    static DateTime lastHpAction = DateTime.MinValue;
    static DateTime lastManaAction = DateTime.MinValue;

    static bool programRunning = true;
    static bool itemDragInProgress = false;
    static int currentDragCount = 0;

    // Character center coordinates (from click waypoint function)
    static int characterCenterX = 300; // Adjust these values based on your game window
    static int characterCenterY = 280;

    // Backpack coordinates (first sqm)
    static int backpackX = 800; // Adjust based on your backpack position
    static int backpackY = 255;

    // Ground coordinates (where items are dropped)
    static int groundX = 300; // This should be the center of the ground where items are dropped
    static int groundY = 280;
    static CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    static void Main()
    {
        Console.WriteLine("Starting RealeraDX Auto-Potions...");
        ShowAllProcessesWithWindows();

        // Find the process
        Process process = FindRealeraProcess();
        if (process == null)
        {
            Console.WriteLine("RealeraDX process not found!");
            return;
        }

        // Get process handle
        processHandle = OpenProcess(PROCESS_ALL_ACCESS, false, process.Id);
        moduleBase = process.MainModule.BaseAddress;

        // Find window handle
        FindRealeraWindow(process);

        Console.WriteLine($"Found RealeraDX process (ID: {process.Id})");
        Console.WriteLine($"Window handle: {targetWindow}");
        Console.WriteLine("\nThresholds:");
        Console.WriteLine($"HP: {HP_THRESHOLD}%");
        Console.WriteLine($"Mana: {MANA_THRESHOLD}");
        Console.WriteLine($"Soul: {SOUL_THRESHOLD} (absolute value)");
        Console.WriteLine("\nPress Q to quit, E to drag items from backpack to ground (30x), R to drag items from ground to backpack (30x)");
        
        // Main loop
        while (programRunning)
        {
            try
            {
                // Check for quit key
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    

                    // In Main function, when checking for Q key:
                    if (key == ConsoleKey.Q)
                    {
                        Console.WriteLine("\nQuitting...");
                        programRunning = false;

                        // Cancel any ongoing operations
                        cancellationTokenSource.Cancel();

                        // Wait a bit for any running threads to finish gracefully
                        Thread.Sleep(1000);

                        // Force close the application if needed
                        Environment.Exit(0);
                    }
                    else if (key == ConsoleKey.E)
                    {
                        if (!itemDragInProgress)
                        {
                            Console.WriteLine("Starting to drag items from backpack to ground (30x)...");
                            currentDragCount = 0;
                            // Start dragging in a new thread to not block the main loop
                            Task.Run(() => StartItemDragging(false)); // false = backpack to ground
                        }
                        else
                        {
                            cancellationTokenSource.Cancel();
                            itemDragInProgress = false;
                            Console.WriteLine("Item dragging already in progress...");
                        }
                    }
                    else if (key == ConsoleKey.R)
                    {
                        if (!itemDragInProgress)
                        {
                            Console.WriteLine("Starting to drag items from ground to backpack (30x)...");
                            currentDragCount = 0;
                            // Start dragging in a new thread to not block the main loop
                            Task.Run(() => StartItemDragging(true)); // true = ground to backpack
                        }
                        else
                        {
                            cancellationTokenSource.Cancel();
                            itemDragInProgress = false;
                            Console.WriteLine("Item dragging already in progress...");
                        }
                    }
                }

                // Read memory values
                ReadMemoryValues();

                // Calculate percentages
                double hpPercent = (curHP / maxHP) * 100;
                double manaPercent = (curMana / maxMana) * 100;
                double mana = curMana;

                // HP check
                if (hpPercent <= HP_THRESHOLD)
                {
                    if ((DateTime.Now - lastHpAction).TotalMilliseconds >= 2000)
                    {
                        SendKeyPress(VK_F1);
                        lastHpAction = DateTime.Now;
                        Console.WriteLine($"HP low ({hpPercent:F1}%) - pressed F1");
                    }
                }

                // Mana check
                if (mana <= MANA_THRESHOLD)
                {
                    if ((DateTime.Now - lastManaAction).TotalMilliseconds >= 2000)
                    {
                        SendKeyPress(VK_F3);
                        lastManaAction = DateTime.Now;
                        Console.WriteLine($"Mana low ({mana:F1}) - pressed F3");
                    }
                }
                // Mana & Soul check
                else if (curMana >= MANA_THRESHOLD && curSoul > SOUL_THRESHOLD)
                {
                    if ((DateTime.Now - lastManaAction).TotalMilliseconds >= 2000)
                    {
                        SendKeyPress(VK_F2);
                        lastManaAction = DateTime.Now;
                        Console.WriteLine($"Mana>900 ({curMana:F0}), Soul>{SOUL_THRESHOLD} ({curSoul:F1}) - pressed F2");
                    }
                }

                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Thread.Sleep(1000);
            }
        }
    }



    static void StartItemDragging(bool reverseDirection)
    {
        try
        {
            itemDragInProgress = true;
            string direction = reverseDirection ? "ground to backpack" : "backpack to ground";
            Console.WriteLine($"Item dragging task started ({direction})...");

            // Get the window's client area coordinates
            RECT clientRect;
            GetClientRect(targetWindow, out clientRect);
            int localX = backpackX;
            int localY = backpackY;
            if (reverseDirection)
            {
                localX += 125;
                localY += 125;
            }
            // Convert local coordinates to screen coordinates
            POINT groundPoint = new POINT { X = groundX, Y = groundY };
            POINT backpackPoint = new POINT { X = localX, Y = localY };

            ClientToScreen(targetWindow, ref groundPoint);
            ClientToScreen(targetWindow, ref backpackPoint);

            // Determine source and destination based on direction
            POINT sourcePoint, destPoint;
            if (reverseDirection)
            {
                sourcePoint = groundPoint;   // From ground
                destPoint = backpackPoint;   // To backpack
            }
            else
            {
                sourcePoint = backpackPoint; // From backpack
                destPoint = groundPoint;     // To ground
            }

            // Perform 30 drags
            const int MAX_DRAGS = 8;
            for (currentDragCount = 1; currentDragCount <= MAX_DRAGS; currentDragCount++)
            {
                // Check if cancellation was requested (for graceful shutdown)
                if (cancellationTokenSource.Token.IsCancellationRequested || !programRunning)
                {
                    Console.WriteLine($"Item dragging interrupted during drag #{currentDragCount}");
                    cancellationTokenSource = new CancellationTokenSource();
                    break;
                }

                // Simulate dragging an item
                DragItem(sourcePoint.X, sourcePoint.Y, destPoint.X, destPoint.Y);
                Console.WriteLine($"Drag #{currentDragCount} completed... ({currentDragCount}/{MAX_DRAGS})");

                // Wait between drags (increased for debugging visibility)
                // Use cancellation token with timeout to allow for graceful shutdown
                try
                {
                    cancellationTokenSource.Token.WaitHandle.WaitOne(100);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Item dragging cancelled");
                    cancellationTokenSource = new CancellationTokenSource();
                    break;
                }
            }

            if (currentDragCount > MAX_DRAGS)
            {
                Console.WriteLine($"All {MAX_DRAGS} item drags completed ({direction}).");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during item dragging: {ex.Message}");
        }
        finally
        {
            itemDragInProgress = false;
            currentDragCount = 0;
        }
    }

    // Add this import with your other DLL imports
    [DllImport("user32.dll")]
    static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    // Simple DragItem function without smooth dragging
    static void DragItem(int fromX, int fromY, int toX, int toY)
    {
        // Convert screen coordinates to client coordinates
        POINT sourcePoint = new POINT { X = fromX, Y = fromY };
        POINT destPoint = new POINT { X = toX, Y = toY };

        ScreenToClient(targetWindow, ref sourcePoint);
        ScreenToClient(targetWindow, ref destPoint);

        // Create lParam for coordinates
        IntPtr lParamFrom = (IntPtr)((sourcePoint.Y << 16) | (sourcePoint.X & 0xFFFF));
        IntPtr lParamTo = (IntPtr)((destPoint.Y << 16) | (destPoint.X & 0xFFFF));

        // Step 1: Move to source position
        PostMessage(targetWindow, WM_MOUSEMOVE, IntPtr.Zero, lParamFrom);
        Thread.Sleep(1);

        // Step 2: Click and hold
        PostMessage(targetWindow, WM_LBUTTONDOWN, IntPtr.Zero, lParamFrom);
        Thread.Sleep(1);

        // Step 3: Move to destination (drag)
        PostMessage(targetWindow, WM_MOUSEMOVE, IntPtr.Zero, lParamTo);
        Thread.Sleep(1);

        // Step 4: Release mouse button
        PostMessage(targetWindow, WM_LBUTTONUP, IntPtr.Zero, lParamTo);
        Thread.Sleep(1);

        Console.WriteLine($"Dragged item from ({fromX}, {fromY}) to ({toX}, {toY})");
    }

    static Process FindRealeraProcess()
    {
        var processes = Process
            .GetProcesses()
            .Where(p => p.ProcessName == processName)
            .ToArray();

        if (processes.Length == 0)
        {
            Console.WriteLine($"Process '{processName}' not found.");
            return null;
        }

        // First, try to find process with "Knajtka Martynka" in title
        var targetProcess = processes.FirstOrDefault(p =>
            !string.IsNullOrEmpty(p.MainWindowTitle) &&
            p.MainWindowTitle.Contains("Knajtka Martynka", StringComparison.OrdinalIgnoreCase));

        if (targetProcess != null)
        {
            Console.WriteLine($"Found target process: {targetProcess.ProcessName} (ID: {targetProcess.Id})");
            Console.WriteLine($"Window Title: {targetProcess.MainWindowTitle}");
            return targetProcess;
        }
        else if (processes.Length == 1)
        {
            var process = processes[0];
            Console.WriteLine($"One process found: {process.ProcessName} (ID: {process.Id})");
            Console.WriteLine($"Window Title: {process.MainWindowTitle}");
            Console.WriteLine("WARNING: Process doesn't contain 'Knajtka Martynka' in title!");
            return process;
        }
        else
        {
            Console.WriteLine($"Multiple processes found with name '{processName}':");
            for (int i = 0; i < processes.Length; i++)
            {
                Console.WriteLine($"{i + 1}: ID={processes[i].Id}, Name={processes[i].ProcessName}, Window Title={processes[i].MainWindowTitle}, StartTime={(processes[i].StartTime)}");
            }
            Console.WriteLine("Enter the number of the process you want to select (1-9):");
            string input = Console.ReadLine();
            if (
                int.TryParse(input, out int choice)
                && choice >= 1
                && choice <= processes.Length
            )
            {
                var selectedProc = processes[choice - 1];
                Console.WriteLine($"Selected process: {selectedProc.ProcessName} (ID: {selectedProc.Id})");
                Console.WriteLine($"Window Title: {selectedProc.MainWindowTitle}");
                return selectedProc;
            }
            else
            {
                Console.WriteLine("Invalid selection. Please try again.");
                return null;
            }
        }
    }

    static void FindRealeraWindow(Process process)
    {
        EnumWindows(
            (hWnd, lParam) =>
            {
                uint windowProcessId;
                GetWindowThreadProcessId(hWnd, out windowProcessId);
                if (windowProcessId == (uint)process.Id)
                {
                    StringBuilder sb = new StringBuilder(256);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    if (sb.ToString().Contains("Realera 8.0 - Knajtka Martynka"))
                    {
                        targetWindow = hWnd;
                        return false;
                    }
                }
                return true;
            },
            IntPtr.Zero
        );
    }

    static void ReadMemoryValues()
    {
        curHP = ReadDouble(HP_OFFSET);
        maxHP = ReadDouble(MAX_HP_OFFSET);
        curMana = ReadDouble(MANA_OFFSET);
        maxMana = ReadDouble(MAX_MANA_OFFSET);
        curSoul = ReadDouble(SOUL_OFFSET);
    }

    static double ReadDouble(int offset)
    {
        IntPtr address = IntPtr.Add(moduleBase, (int)BASE_ADDRESS);
        byte[] buffer = new byte[4];

        if (ReadProcessMemory(processHandle, address, buffer, 4, out _))
        {
            IntPtr finalAddress = BitConverter.ToInt32(buffer, 0);
            finalAddress = IntPtr.Add(finalAddress, offset);

            byte[] valueBuffer = new byte[8];
            if (ReadProcessMemory(processHandle, finalAddress, valueBuffer, 8, out _))
            {
                return BitConverter.ToDouble(valueBuffer, 0);
            }
        }
        return 0;
    }

    static void SendKeyPress(int key)
    {
        SendMessage(targetWindow, WM_KEYDOWN, key, IntPtr.Zero);
        Thread.Sleep(10);
        SendMessage(targetWindow, WM_KEYUP, key, IntPtr.Zero);
    }

    static void ShowAllProcessesWithWindows()
    {
        Console.WriteLine("\n=== REALERA PROCESSES WITH WINDOWS ===");
        var processes = Process.GetProcesses()
            .Where(p => p.ProcessName.Contains("Realera", StringComparison.OrdinalIgnoreCase))
            .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
            .OrderBy(p => p.ProcessName);

        foreach (var process in processes)
        {
            try
            {
                Console.WriteLine($"Process: {process.ProcessName}");
                Console.WriteLine($"  ID: {process.Id}");
                Console.WriteLine($"  Main Window Title: '{process.MainWindowTitle}'");
                Console.WriteLine($"  Window Handle: {process.MainWindowHandle}");
                Console.WriteLine();
            }
            catch
            {
                // Some processes might not be accessible
            }
        }
        Console.WriteLine("=======================================\n");
    }
}
