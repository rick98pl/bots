using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System;
using System.Buffers.Text;
class Program
{
    [DllImport("kernel32.dll")]
    static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int processId);
    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("kernel32.dll")]
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
    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    const int PROCESS_WM_READ = 0x0010;
    const uint WM_KEYDOWN = 0x0100;
    const uint WM_KEYUP = 0x0101;
    const int VK_LEFT = 0x25;
    const int VK_UP = 0x26;
    const int VK_RIGHT = 0x27;
    const int VK_DOWN = 0x28;
    static class Keys
    {
        public static readonly Dictionary<string, int> KeyMap = new Dictionary<string, int>
        {
            { "F1", 0x70 },
            { "F2", 0x71 },
            { "F3", 0x72 },
            { "F4", 0x73 },
            { "F5", 0x74 },
            { "F6", 0x75 },
            { "F7", 0x76 },
            { "F8", 0x77 },
            { "F9", 0x78 },
            { "F10", 0x79 },
            { "F11", 0x7A },
            { "F12", 0x7B }
        };
        public static int GetKeyCode(string keyName) =>
            KeyMap.ContainsKey(keyName) ? KeyMap[keyName] : -1;
    }
    const int DEFAULT_HP_THRESHOLD = 50;
    const int DEFAULT_BEEP_HP_THRESHOLD = 30;
    const int DEFAULT_MANA_THRESHOLD = 70;
    const string DEFAULT_HP_KEY_NAME = "F1";
    const string DEFAULT_MANA_KEY_NAME = "F2";
    static int DEFAULT_HP_KEY => Keys.GetKeyCode(DEFAULT_HP_KEY_NAME);
    static int DEFAULT_MANA_KEY => Keys.GetKeyCode(DEFAULT_MANA_KEY_NAME);
    static IntPtr targetWindow = IntPtr.Zero;
    static DateTime lastHpActionTime = DateTime.MinValue;
    static DateTime lastManaActionTime = DateTime.MinValue;
    static Random random = new Random();
    static IntPtr xAddressOffset = (IntPtr)0x009435FC;
    static IntPtr yAddressOffset = (IntPtr)0x00943600;
    static IntPtr zAddressOffset = (IntPtr)0x00943604;
    static IntPtr targetIdOffset = (IntPtr)0x009432D4;
    static IntPtr followOffset = (IntPtr)0x00943380;
    static double curHP = 0,
        maxHP = 1,
        curMana = 0,
        maxMana = 1;
    static int posX = 0,
        posY = 0,
        posZ = 0,
        targetId = 0,
        follow = 0;
    static bool memoryReadActive = false;
    static bool programRunning = true;
    static bool autoPotionActive = true;
    static bool isRecording = false;
    static bool isPlaying = false;
    static bool shouldRestartMemoryThread = false;
    static ConcurrentDictionary<string, bool> threadFlags = new ConcurrentDictionary<
        string,
        bool
    >();
    static object memoryLock = new object();
    static List<Coordinate> recordedCoords = new List<Coordinate>();
    static string cordsFilePath = "cords.json";
    static CoordinateData loadedCoords = null;
    static Process selectedProcess = null;
    static IntPtr processHandle = IntPtr.Zero;
    static IntPtr moduleBase = IntPtr.Zero;
    struct Variable
    {
        public string Name;
        public IntPtr BaseAddress;
        public List<int> Offsets;
        public string Type;
    }
    class Coordinate
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }
    class CoordinateData
    {
        public List<Coordinate> cords { get; set; } = new List<Coordinate>();
    }
    static int currentCoordIndex = -1;
    static int totalCoords = 0;
    static Coordinate currentTarget = null;
    static bool debug = true;
    static int debugTime = 2;

    static void Main()
    {
        Console.WriteLine($"Default HP Key: {DEFAULT_HP_KEY_NAME}");
        Console.WriteLine($"Default Mana Key: {DEFAULT_MANA_KEY_NAME}");
        threadFlags["recording"] = false;
        threadFlags["playing"] = false;
        threadFlags["autopot"] = true;

        // Initialize the sound system
        InitializeSounds();

        if (!File.Exists(cordsFilePath))
        {
            SaveCoordinates();
        }

        // Rest of the Main method unchanged
        string processName = "RealeraDX";
        while (programRunning)
        {
            {
                while (selectedProcess == null)
                {
                    var processes = Process
                        .GetProcesses()
                        .Where(p => p.ProcessName == processName)
                        .ToArray();
                    if (processes.Length == 0)
                    {
                        Console.WriteLine($"Process '{processName}' not found.");
                        Sleep(500);
                        continue;
                    }
                    else if (processes.Length == 1)
                    {
                        selectedProcess = processes[0];
                        Console.WriteLine(
                            $"One process found: {selectedProcess.ProcessName} (ID: {selectedProcess.Id})"
                        );
                        Console.WriteLine($"Window Title: {selectedProcess.MainWindowTitle}");
                    }
                    else
                    {
                        Console.WriteLine($"Multiple processes found with name '{processName}':");
                        for (int i = 0; i < processes.Length; i++)
                        {
                            Console.WriteLine(
                                $"{i + 1}: ID={processes[i].Id}, Name={processes[i].ProcessName}, Window Title={processes[i].MainWindowTitle}, StartTime={(processes[i].StartTime)}"
                            );
                        }
                        Console.WriteLine(
                            "Enter the number of the process you want to select (1-9):"
                        );
                        string input = Console.ReadLine();
                        if (
                            int.TryParse(input, out int choice)
                            && choice >= 1
                            && choice <= processes.Length
                        )
                        {
                            selectedProcess = processes[choice - 1];
                            Console.WriteLine(
                                $"Selected process: {selectedProcess.ProcessName} (ID: {selectedProcess.Id})"
                            );
                            Console.WriteLine($"Window Title: {selectedProcess.MainWindowTitle}");
                        }
                        else
                        {
                            Console.WriteLine("Invalid selection. Please try again.");
                        }
                    }
                }
            }
            FindRealeraWindow(selectedProcess);
            processHandle = OpenProcess(PROCESS_WM_READ, false, selectedProcess.Id);
            moduleBase = selectedProcess.MainModule.BaseAddress;

            GetClientRect(targetWindow, out RECT windowRect);
            int windowHeight = windowRect.Bottom - windowRect.Top;
            Console.WriteLine($"[DEBUG] Detected window height: {windowHeight}px");
            smallWindow = windowHeight < 1200;
            Console.WriteLine($"[DEBUG] Using {(smallWindow ? "small window (1080p)" : "large window (1440p)")} settings");

            StartWorkerThreads();
            while (memoryReadActive && !shouldRestartMemoryThread)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    HandleUserInput(key);
                }
                Sleep(250);
            }
            if (shouldRestartMemoryThread)
            {
                shouldRestartMemoryThread = false;
                StopWorkerThreads();
                selectedProcess = null;
            }
        }
    }
    static void StartWorkerThreads()
    {
        memoryReadActive = true;
        Thread memoryThread = new Thread(MemoryReadingThread);
        memoryThread.IsBackground = true;
        memoryThread.Name = "MemoryReader";
        memoryThread.Start();
        Thread autoPotionThread = new Thread(AutoPotionThread);
        autoPotionThread.IsBackground = true;
        autoPotionThread.Name = "AutoPotion";
        autoPotionThread.Start();
        Console.WriteLine("Worker threads started successfully");
    }
    static void StopWorkerThreads()
    {
        memoryReadActive = false;
        threadFlags["recording"] = false;
        threadFlags["playing"] = false;
        StopPositionAlertSound(); // Stop any playing alert sounds
        Console.WriteLine("Worker threads stopping...");
        Sleep(1000);
    }
    static List<Variable> variables = new List<Variable>
    {
        new Variable
        {
            Name = "Current Mana",
            BaseAddress = (IntPtr)0x009432D0,
            Offsets = new List<int> { 1240 },
            Type = "Double"
        },
        new Variable
        {
            Name = "Current HP",
            BaseAddress = (IntPtr)0x009432D0,
            Offsets = new List<int> { 1184 },
            Type = "Double"
        },
        new Variable
        {
            Name = "Max Mana",
            BaseAddress = (IntPtr)0x009432D0,
            Offsets = new List<int> { 1248 },
            Type = "Double"
        },
        new Variable
        {
            Name = "Max HP",
            BaseAddress = (IntPtr)0x009432D0,
            Offsets = new List<int> { 1192 },
            Type = "Double"
        }
    };
    static void MemoryReadingThread()
    {
        DateTime lastDebugOutputTime = DateTime.MinValue;  // Declare as DateTime type
        const double DEBUG_COOLDOWN_SECONDS = 1.5;
        Console.WriteLine("Memory reading thread started");
        while (memoryReadActive)
        {
            try
            {
                if (selectedProcess.HasExited)
                {
                    shouldRestartMemoryThread = true;
                    break;
                }
                foreach (var variable in variables)
                {
                    try
                    {
                        IntPtr address = IntPtr.Add(moduleBase, (int)variable.BaseAddress);
                        byte[] buffer;
                        if (variable.Offsets.Count > 0)
                        {
                            buffer = new byte[4];
                            if (
                                !ReadProcessMemory(
                                    processHandle,
                                    address,
                                    buffer,
                                    buffer.Length,
                                    out _
                                )
                            )
                                continue;
                            address = (IntPtr)BitConverter.ToInt32(buffer, 0);
                            address = IntPtr.Add(address, variable.Offsets[0]);
                        }
                        buffer = variable.Type == "Double" ? new byte[8] : new byte[4];
                        if (
                            !ReadProcessMemory(processHandle, address, buffer, buffer.Length, out _)
                        )
                            continue;
                        double value =
                            variable.Type == "Double"
                                ? BitConverter.ToDouble(buffer, 0)
                                : BitConverter.ToInt32(buffer, 0);
                        lock (memoryLock)
                        {
                            if (variable.Name.Contains("HP") && !variable.Name.Contains("Max"))
                                curHP = value;
                            if (variable.Name.Contains("Mana") && !variable.Name.Contains("Max"))
                                curMana = value;
                            if (variable.Name.Contains("Max HP"))
                                maxHP = value;
                            if (variable.Name.Contains("Max Mana"))
                                maxMana = value;
                        }
                    }
                    catch { }
                }
                lock (memoryLock)
                {
                    posX = ReadInt32(processHandle, moduleBase, xAddressOffset);
                    posY = ReadInt32(processHandle, moduleBase, yAddressOffset);
                    posZ = ReadInt32(processHandle, moduleBase, zAddressOffset);
                    targetId = ReadInt32(processHandle, moduleBase, targetIdOffset);
                    follow = ReadInt32(processHandle, moduleBase, followOffset);
                    if (threadFlags["recording"])
                    {
                        RecordCoordinate(posX, posY, posZ);
                    }
                }

                lock (memoryLock)
                {
                    // Update the chase tracker with current position and target information
                    chaseTracker.Update(posX, posY, posZ, targetId);

                    // Debug output for monitoring chase status with cooldown
                    if (debug && targetId != 0)
                    {
                        // Check if enough time has passed since the last debug output
                        DateTime now = DateTime.Now;
                        if ((now - lastDebugOutputTime).TotalSeconds >= DEBUG_COOLDOWN_SECONDS)
                        {
                            Console.WriteLine($"[DEBUG] In chase: targetId={targetId}, position=({posX},{posY},{posZ})");
                            lastDebugOutputTime = now;
                        }
                    }
                }

                // Check position distance if we have loaded coordinates
                if (loadedCoords != null && loadedCoords.cords.Count > 0 && threadFlags["playing"])
                {
                    CheckPositionDistance(loadedCoords.cords);
                }

                Sleep(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Memory reading error: {ex.Message}");
                shouldRestartMemoryThread = true;
                break;
            }
        }
        Console.WriteLine("Memory reading thread exited");
    }

    static void AutoPotionThread()
    {
        Console.WriteLine("Auto-potion thread started");
        while (memoryReadActive)
        {
            try
            {
                if (threadFlags["autopot"] && targetWindow != IntPtr.Zero)
                {
                    var now = DateTime.Now;
                    var thresholdms = 1000;
                    double hpPercent,
                        manaPercent;
                    lock (memoryLock)
                    {
                        hpPercent = (curHP / maxHP) * 100;
                        manaPercent = (curMana / maxMana) * 100;
                    }
                    if (hpPercent == 0)
                    {
                        Sleep(1);
                        continue;
                    }

                    if (hpPercent <= DEFAULT_BEEP_HP_THRESHOLD)
                    {
                        StartPositionAlertSound();
                    }

                    if (hpPercent <= DEFAULT_HP_THRESHOLD)
                    {
                        if ((now - lastHpActionTime).TotalMilliseconds >= thresholdms)
                        {
                            Console.WriteLine(
                                $"⚠ HP below threshold ({DEFAULT_HP_THRESHOLD}%), current HP: {curHP}/{maxHP} ({hpPercent:F1}%), sending {DEFAULT_HP_KEY_NAME}"
                            );
                            SendKeyPress(DEFAULT_HP_KEY);
                            lastHpActionTime = now.AddMilliseconds(random.Next(0, 100));
                        }
                    }
                    else
                    {
                        //StopPositionAlertSound();
                    }
                    if (manaPercent <= DEFAULT_MANA_THRESHOLD)
                    {
                        if ((now - lastManaActionTime).TotalMilliseconds >= thresholdms)
                        {
                            Console.WriteLine(
                                $"⚠ Mana below threshold ({DEFAULT_MANA_THRESHOLD}%), sending {DEFAULT_MANA_KEY_NAME}"
                            );
                            SendKeyPress(DEFAULT_MANA_KEY);
                            lastManaActionTime = now.AddMilliseconds(random.Next(0, 100));
                        }
                    }
                }
                Sleep(100);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Auto-potion error: {ex.Message}");
                Sleep(1000);
            }
        }
        Console.WriteLine("Auto-potion thread exited");
    }
    static void DisplayStats()
    {
        Console.Clear();
        double hpPercent;
        double manaPercent;
        int currentX,
            currentY,
            currentZ;
        lock (memoryLock)
        {
            hpPercent = (curHP / maxHP) * 100;
            manaPercent = (curMana / maxMana) * 100;
            currentX = posX;
            currentY = posY;
            currentZ = posZ;
        }
        Console.WriteLine("RealeraDX - Live Stats:\n");
        Console.WriteLine("{0,-20} {1,15}", "Metric", "Value");
        Console.WriteLine(new string('-', 40));
        Console.WriteLine("{0,-20} {1,15:F0}", "Current HP", curHP);
        Console.WriteLine("{0,-20} {1,15:F0}", "Max HP", maxHP);
        Console.WriteLine("{0,-20} {1,15:F1}%", "HP %", hpPercent);
        Console.WriteLine("{0,-20} {1,15:F0}", "Current Mana", curMana);
        Console.WriteLine("{0,-20} {1,15:F0}", "Max Mana", maxMana);
        Console.WriteLine("{0,-20} {1,15:F1}%", "Mana %", manaPercent);
        Console.WriteLine("{0,-20} {1,15:F0}", "targetId", targetId);
        Console.WriteLine("{0,-20} {1,15:F0}", "follow", follow);
        Console.WriteLine(new string('-', 40));
        Console.WriteLine($"Position: X={currentX}, Y={currentY}, Z={currentZ}");
        if (threadFlags["recording"])
        {
            Console.WriteLine("🔴 Recording coordinates...");
            Console.WriteLine($"Coordinates recorded: {recordedCoords.Count}");
        }
        if (threadFlags["playing"] && currentTarget != null)
        {
            Console.WriteLine("\n▶️ PLAYING PATH:");
            Console.WriteLine(new string('-', 40));
            Console.WriteLine(
                $"Progress: {currentCoordIndex + 1}/{totalCoords} ({(((float)(currentCoordIndex + 1) / totalCoords) * 100):F1}%)"
            );
            Console.WriteLine($"Current: X={currentX}, Y={currentY}, Z={currentZ}");
            Console.WriteLine(
                $"Target:  X={currentTarget.X}, Y={currentTarget.Y}, Z={currentTarget.Z}"
            );
            int distanceX = Math.Abs(currentTarget.X - currentX);
            int distanceY = Math.Abs(currentTarget.Y - currentY);
            Console.WriteLine($"Distance: {distanceX + distanceY} steps");
            int barLength = 20;
            int progress = (int)Math.Round(
                (double)(currentCoordIndex + 1) / totalCoords * barLength
            );
            Console.Write("[");
            for (int i = 0; i < barLength; i++)
            {
                Console.Write(i < progress ? "█" : " ");
            }
            Console.WriteLine($"] {(((float)(currentCoordIndex + 1) / totalCoords) * 100):F1}%");
        }
        Console.WriteLine("\nActive Features:");
        Console.WriteLine($"Auto-Potions: {(threadFlags["autopot"] ? "✅ ON" : "❌ OFF")} (A)");
        Console.WriteLine($"Recording: {(threadFlags["recording"] ? "✅ ON" : "❌ OFF")} (R)");
        Console.WriteLine($"Playback: {(threadFlags["playing"] ? "✅ ON" : "❌ OFF")} (P)");
        Console.WriteLine("\nCommands:");
        Console.WriteLine("R - Start/Stop Recording");
        Console.WriteLine("P - Start/Stop Path Playback");
        Console.WriteLine("A - Toggle Auto-Potions");
        Console.WriteLine("Q - Quit");
    }
    static void HandleUserInput(ConsoleKey key)
    {
        switch (key)
        {
            case ConsoleKey.R:
                if (!threadFlags["recording"] && !threadFlags["playing"])
                {
                    Console.Write("Start recording coordinates? (y/n): ");
                    string confirm = Console.ReadLine();
                    if (confirm.ToLower() == "y")
                    {
                        threadFlags["recording"] = true;
                        recordedCoords.Clear();
                        Console.WriteLine("Started recording coordinates...");
                    }
                }
                else if (threadFlags["recording"])
                {
                    threadFlags["recording"] = false;
                    SaveCoordinates();
                    Console.WriteLine("Stopped recording and saved coordinates.");
                }
                break;
            case ConsoleKey.P:
                if (!threadFlags["playing"] && !threadFlags["recording"])
                {
                    if (File.Exists(cordsFilePath))
                    {
                        threadFlags["playing"] = true;
                        StartPathPlayback();
                    }
                    else
                    {
                        Console.WriteLine("cords.json not found!");
                        Sleep(1000);
                    }
                }
                else if (threadFlags["playing"])
                {
                    threadFlags["playing"] = false;
                    Console.WriteLine("Path playback stopped.");
                }
                break;
            case ConsoleKey.A:
                threadFlags["autopot"] = !threadFlags["autopot"];
                Console.WriteLine(
                    $"Auto-potions {(threadFlags["autopot"] ? "enabled" : "disabled")}"
                );
                break;
            case ConsoleKey.S: // Add a key to manually stop position alert sound
                StopPositionAlertSound();
                Console.WriteLine("Position alert sound stopped manually.");
                break;
            case ConsoleKey.Q:
                programRunning = false;
                memoryReadActive = false;
                threadFlags["recording"] = false;
                threadFlags["playing"] = false;
                threadFlags["autopot"] = false;
                // Make sure to stop any playing sounds
                StopPositionAlertSound();
                break;
        }
    }
    static void StartPathPlayback()
    {
        Thread playThread = new Thread(
            () =>
            {
                try
                {
                    PlayCoordinates();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Path playback error: {ex.Message}");
                }
                finally
                {
                    threadFlags["playing"] = false;
                }
            }
        );
        playThread.IsBackground = true;
        playThread.Name = "PathPlayer";
        playThread.Start();
    }
    static int ReadInt32(IntPtr handle, IntPtr moduleBase, IntPtr offset)
    {
        IntPtr address = IntPtr.Add(moduleBase, (int)offset);
        byte[] buffer = new byte[4];
        if (ReadProcessMemory(handle, address, buffer, buffer.Length, out _))
            return BitConverter.ToInt32(buffer, 0);
        return 0;
    }
    // Variables to track F6 key state
    private static DateTime lastF6Press = DateTime.MinValue;
    private static bool canPressF6 = true;
    private static readonly TimeSpan F6Cooldown = TimeSpan.FromSeconds(0.6);

    static void SendKeyPress(int key)
    {
        if (key == 0x75) // F6 key
        {
            DateTime currentTime = DateTime.Now;

            if (!canPressF6 || (currentTime - lastF6Press) < F6Cooldown)
            {
                Console.WriteLine("F6 key blocked - cooldown active");
                return;
            }

            Console.WriteLine("F6 key pressed - starting cooldown");
            lastF6Press = currentTime;
            canPressF6 = false;

            System.Threading.Timer cooldownTimer = null;
            cooldownTimer = new System.Threading.Timer((state) =>
            {
                canPressF6 = true;
                Console.WriteLine("F6 key cooldown finished");
                cooldownTimer?.Dispose();
            }, null, F6Cooldown, TimeSpan.Zero);
        }

        PostMessage(targetWindow, WM_KEYDOWN, (IntPtr)key, IntPtr.Zero);
        Sleep(random.Next(10, 25));
        PostMessage(targetWindow, WM_KEYUP, (IntPtr)key, IntPtr.Zero);
    }

    static void InstantSendKeyPress(int key)
    {
        PostMessage(targetWindow, WM_KEYDOWN, (IntPtr)key, IntPtr.Zero);
        Sleep(random.Next(10, 25));
        PostMessage(targetWindow, WM_KEYUP, (IntPtr)key, IntPtr.Zero);
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
                    if (sb.ToString().Contains("Realera 8.0"))
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
    static void SaveCoordinates()
    {
        CoordinateData data = new CoordinateData { cords = recordedCoords };
        string json = JsonSerializer.Serialize(
            data,
            new JsonSerializerOptions { WriteIndented = true }
        );
        File.WriteAllText(cordsFilePath, json);
    }
    static void RecordCoordinate(int x, int y, int z)
    {
        if (
            recordedCoords.Count == 0
            || recordedCoords.Last().X != x
            || recordedCoords.Last().Y != y
            || recordedCoords.Last().Z != z
        )
        {
            recordedCoords.Add(new Coordinate { X = x, Y = y, Z = z });
        }
    }
    private static void MoveCharacterTowardsWaypoint(
    int currentX,
    int currentY,
    int waypointX,
    int waypointY
)
    {
        int diffX = waypointX - currentX;
        int diffY = waypointY - currentY;

        // Determine which direction to move first
        bool moveXFirst = Math.Abs(diffX) >= Math.Abs(diffY);

        if (moveXFirst)
        {
            // Move in X direction first if it's farther
            if (diffX > 0)
            {
                Console.WriteLine("[DEBUG] Moving EAST with arrow key");
                SendKeyPress(VK_RIGHT);
            }
            else if (diffX < 0)
            {
                Console.WriteLine("[DEBUG] Moving WEST with arrow key");
                SendKeyPress(VK_LEFT);
            }
        }
        else
        {
            // Move in Y direction first if it's farther
            if (diffY > 0)
            {
                Console.WriteLine("[DEBUG] Moving SOUTH with arrow key");
                SendKeyPress(VK_DOWN);
            }
            else if (diffY < 0)
            {
                Console.WriteLine("[DEBUG] Moving NORTH with arrow key");
                SendKeyPress(VK_UP);
            }
        }

        // Only make one move per call to ensure step-by-step movement
        // The function will be called again on the next loop iteration
    }

    static bool shouldClickAround = true;
    private static int previousTargetId = 0;
    static ChasePathTracker chaseTracker = new ChasePathTracker();
    static Coordinate previousChasePosition = null;
    static bool isReturningFromChase = false;
    static private HashSet<int> clickedAroundTargets = new HashSet<int>();
    static private int lastTrackedTargetId = 0;


    static void PlayCoordinates()
    {
        //Thread.Sleep(1500);
        UpdateUIPositions();
        Console.WriteLine("Path playback starting...");
        string json = File.ReadAllText(cordsFilePath);
        loadedCoords = JsonSerializer.Deserialize<CoordinateData>(json);
        if (loadedCoords == null || loadedCoords.cords.Count == 0)
        {
            Console.WriteLine("No coordinates found in cords.json!");
            threadFlags["playing"] = false;
            return;
        }
        List<Coordinate> waypoints = loadedCoords.cords;
        totalCoords = waypoints.Count;
        HashSet<int> blacklistedTargets = new HashSet<int>();
        bool isReversed = false;
        Console.WriteLine($"Loaded {totalCoords} coordinates from cords.json");
        int currentX,
            currentY,
            currentZ;
        lock (memoryLock)
        {
            currentX = posX;
            currentY = posY;
            currentZ = posZ;
        }
        currentCoordIndex = FindClosestWaypointIndex(waypoints, currentX, currentY, currentZ);
        Console.WriteLine($"[DEBUG] Starting at closest waypoint: index {currentCoordIndex}");
        while (threadFlags["playing"])
        {
            try
            {
                int currentTargetId;
                lock (memoryLock)
                {
                    currentX = posX;
                    currentY = posY;
                    currentZ = posZ;
                    currentTargetId = targetId;
                }
                if (previousTargetId != 0 && currentTargetId == 0)
                {
                    lock (memoryLock)
                    {
                        // Force update the chase tracker with latest position after killing the monster
                        chaseTracker.Update(posX, posY, posZ, targetId);
                    }
                    if (chaseTracker.ShouldReturnToStart())
                    {
                        Console.WriteLine("[DEBUG] Need to return to chase start position");

                        // We don't need to do anything else here - the FindNextWaypoint method
                        // will handle getting the return position and navigating there
                    }
                    else
                    {
                        Console.WriteLine("[DEBUG] No need to return to chase start position");
                    }

                    ToggleRing(targetWindow, false);
                    Sleep(1);

                    if (!clickedAroundTargets.Contains(previousTargetId) && previousTargetId != 0)
                    {
                        Console.WriteLine($"[DEBUG] Fight finished, calling ClickAroundCharacter for target ID: {previousTargetId}");
                        clickedAroundTargets.Add(previousTargetId); // Track that we've clicked for this target
                        ClickAroundCharacter(targetWindow);
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] Already clicked around for target ID: {previousTargetId}, skipping");
                    }
                    Sleep(1);
                    lock (memoryLock)
                    {
                        currentTargetId = targetId;
                    }
                }
                previousTargetId = currentTargetId;
                lock (memoryLock)
                {
                    currentTargetId = targetId;
                }

                //if (currentTargetId != 0 && IsTargetBlacklisted(currentTargetId))
                //{
                //    Console.WriteLine($"[DEBUG] Skipping blacklisted target ID: {currentTargetId}");
                //    InstantSendKeyPress(VK_F6);
                //    Sleep(1);
                //    continue;
                //}

                //if (currentTargetId != 0)
                //{
                //    bool wasBlacklisted = CheckMonsterDistanceAndBlacklist();
                //    if (wasBlacklisted)
                //    {
                //        // Target was blacklisted, move on to next loop iteration
                //        continue;
                //    }
                //}

                //if (DateTime.Now.Second % 5 == 0)
                //{
                //    CleanBlacklistedTargets();
                //}

                if (currentTargetId != 0 && currentTargetId != lastTrackedTargetId)
                {
                    // A new target appeared, clear the tracking
                    Console.WriteLine($"[DEBUG] New target ID detected: {currentTargetId}, resetting click tracking");
                    clickedAroundTargets.Clear();
                    lastTrackedTargetId = currentTargetId;
                }

                if (currentTargetId != 0)
                {

                    ToggleRing(targetWindow, true);
                    Sleep(1);
                    var (monsterX, monsterY, monsterZ, monsterName) = GetTargetMonsterInfo();
                    Sleep(1);

                    if (previousTargetId == 0 && currentTargetId != 0)
                    {
                        previousChasePosition = new Coordinate { X = currentX, Y = currentY, Z = currentZ };
                        Console.WriteLine($"[DEBUG] Chase began at position: X={currentX}, Y={currentY}, Z={currentZ}");
                    }

                    if (blacklistedTargets.Contains(currentTargetId))
                    {

                        Console.WriteLine(
                            $"[DEBUG] Skipping blacklisted target ID: {currentTargetId}"
                        );
                        SendKeyPress(VK_ESCAPE);
                        Sleep(1);
                        continue;
                    }
                    if (
                        !string.IsNullOrEmpty(monsterName)
                        && blacklistedMonsterNames.Contains(monsterName)
                    )
                    {

                        Console.WriteLine(
                            $"[DEBUG] Skipping blacklisted monster: {monsterName}"
                        );
                        SendKeyPress(VK_ESCAPE);
                        Sleep(1);
                        continue;
                    }
                    Sleep(1);
                    lock (memoryLock)
                    {
                        currentTargetId = targetId;
                    }
                    if (!string.IsNullOrEmpty(monsterName))
                    {
                        shouldClickAround = true;
                    }
                    lock (memoryLock)
                    {
                        currentX = posX;
                        currentY = posY;
                        currentZ = posZ;
                    }
                    continue;
                }
                lock (memoryLock)
                {
                    currentTargetId = targetId;
                }
                if (currentTargetId == 0)
                {

                    Console.WriteLine("[DEBUG] No target, pressing F6 to search");
                    SendKeyPress(VK_F6);
                    Sleep(1); //CRUCIAL YOU CANT SPAM F6 because targetId is getting weird!!! even if no mnoster
                }

                lock (memoryLock)
                {
                    currentTargetId = targetId;
                }
                if (currentTargetId != 0)
                {

                    Console.WriteLine($"[DEBUG] Found target: {currentTargetId}");
                    continue;
                }
                Console.WriteLine("[DEBUG] No target found, proceeding with movement");
                Sleep(1);
                Coordinate nextWaypoint = FindNextWaypoint(
                    ref waypoints,
                    currentX,
                    currentY,
                    currentZ,
                    ref currentCoordIndex
                );
                currentTarget = nextWaypoint;
                Console.WriteLine(
                    $"[DEBUG] Moving to waypoint: X={nextWaypoint.X}, Y={nextWaypoint.Y}, Z={nextWaypoint.Z}"
                );
                int distanceX = Math.Abs(nextWaypoint.X - currentX);
                int distanceY = Math.Abs(nextWaypoint.Y - currentY);
                int totalDistance = distanceX + distanceY;
                if (distanceX > 5 || distanceY > 5)
                {

                    Console.WriteLine(
                        $"[DEBUG] Waypoint too far to click: X={nextWaypoint.X}, Y={nextWaypoint.Y}, Z={nextWaypoint.Z}"
                    );

                    Console.WriteLine(
                        $"[DEBUG] Current position: X={currentX}, Y={currentY}, Z={currentZ}"
                    );

                    Console.WriteLine($"[DEBUG] Distance: X={distanceX}, Y={distanceY}");
                    int maxMoves = 5;
                    int movesMade = 0;
                    while ((distanceX > 5 || distanceY > 5) && movesMade < maxMoves)
                    {
                        MoveCharacterTowardsWaypoint(
                            currentX,
                            currentY,
                            nextWaypoint.X,
                            nextWaypoint.Y
                        );
                        Sleep(250);
                        lock (memoryLock)
                        {
                            currentX = posX;
                            currentY = posY;
                            currentZ = posZ;
                        }
                        distanceX = Math.Abs(nextWaypoint.X - currentX);
                        distanceY = Math.Abs(nextWaypoint.Y - currentY);

                        Console.WriteLine(
                            $"[DEBUG] After move #{movesMade + 1}, new position: X={currentX}, Y={currentY}"
                        );

                        Console.WriteLine(
                            $"[DEBUG] New distance: X={distanceX}, Y={distanceY}"
                        );
                        movesMade++;
                        if (distanceX <= 5 && distanceY <= 5)
                        {

                            Console.WriteLine("[DEBUG] Now within clickable range");
                            break;
                        }
                        if (movesMade % 2 == 0)
                        {

                            Console.WriteLine("[DEBUG] Switching movement priority");
                        }
                    }
                    if (distanceX > 5 || distanceY > 5)
                    {

                        Console.WriteLine(
                            "[DEBUG] Still too far after arrow movement, finding a new waypoint"
                        );
                        currentCoordIndex = FindClosestWaypointIndex(
                            waypoints,
                            currentX,
                            currentY,
                            currentZ
                        );
                        continue;
                    }
                }

                if (isReturningFromChase)
                {
                    // First check if we're already close to a waypoint
                    int closestIndex = FindClosestWaypointIndex(waypoints, currentX, currentY, currentZ);
                    Coordinate closestWaypoint = waypoints[closestIndex];
                    int distanceToWaypointX = Math.Abs(closestWaypoint.X - currentX);
                    int distanceToWaypointY = Math.Abs(closestWaypoint.Y - currentY);

                    // If we're now close to a waypoint, stop returning from chase
                    if (distanceToWaypointX < 5 && distanceToWaypointY < 5)
                    {
                        Console.WriteLine($"[DEBUG] While returning from chase, found close waypoint (dx={distanceToWaypointX}, dy={distanceToWaypointY}), stopping return");
                        chaseTracker.CompleteReturn();
                        isReturningFromChase = false;

                        // Skip the movement code below and continue with the next loop iteration
                        // to allow normal waypoint selection on the next cycle
                        continue;
                    }

                    Console.WriteLine("[DEBUG] Using arrow keys to return from chase");
                    // Move step by step with arrow keys instead of clicking
                    MoveCharacterTowardsWaypoint(currentX, currentY, nextWaypoint.X, nextWaypoint.Y);
                    Sleep(100); // Give the game time to process the movement
                    continue;   // Skip the normal waypoint clicking
                }
                Console.WriteLine(
                    $"[DEBUG] Clicking waypoint: X={nextWaypoint.X}, Y={nextWaypoint.Y}, Z={nextWaypoint.Z}"
                );
                bool clickSuccess = ClickWaypoint(nextWaypoint);
                //Sleep(2500);
                Console.WriteLine(
                    $"[DEBUG] Clicking waypoint: X={nextWaypoint.X}, Y={nextWaypoint.Y}, Z={nextWaypoint.Z}"
                );

                // Remove the Sleep(2500) and replace with this destination-checking code
                if (clickSuccess)
                {
                    const int wp_DISTANCE_THRESHOLD = 1; // 2 sqm threshold
                    const int wp_MAX_WAIT_TIME_MS = 4000; // 10 seconds max wait time

                    DateTime wp_startTime = DateTime.Now;
                    bool wp_reachedDestination = false;

                    Console.WriteLine("[DEBUG] Waiting for character to reach destination...");

                    while (DateTime.Now.Subtract(wp_startTime).TotalMilliseconds < wp_MAX_WAIT_TIME_MS)
                    {
                        // Check if in combat
                        int wp_followStatus;
                        int wp_currentTargetId;
                        int wp_currentX, wp_currentY, wp_currentZ;
                        lock (memoryLock)
                        {
                            wp_followStatus = follow;
                            wp_currentTargetId = targetId;
                            wp_currentX = posX;
                            wp_currentY = posY;
                            wp_currentZ = posZ;
                        }

                        if (wp_currentTargetId != 0)
                        {
                            //Console.WriteLine("[DEBUG] Combat detected while moving, stopping wait");
                            break; // Break the wait loop if combat starts
                        }

                        // Calculate current distance to target
                        int wp_currentDiffX = Math.Abs(nextWaypoint.X - wp_currentX);
                        int wp_currentDiffY = Math.Abs(nextWaypoint.Y - wp_currentY);
                        int wp_totalDistance = wp_currentDiffX + wp_currentDiffY;

                        // Occasionally print distance (to avoid log spam)
                        if (DateTime.Now.Millisecond < 100)
                        {
                            //Console.WriteLine($"[DEBUG] Current distance to target: {wp_totalDistance} sqm");
                        }

                        // Check if we've reached the destination
                        if (wp_totalDistance <= wp_DISTANCE_THRESHOLD)
                        {
                            wp_reachedDestination = true;
                            //Console.WriteLine($"[DEBUG] Reached destination (distance: {wp_totalDistance} sqm)");
                            break;
                        }

                        // No Sleep here - just check as fast as possible
                    }

                    if (!wp_reachedDestination)
                    {
                        //Console.WriteLine("[DEBUG] Timed out waiting for destination");
                    }

                    lock (memoryLock)
                    {

                        currentTargetId = targetId;
                    }
                    if (currentTargetId == 0)
                    {
                        SendKeyPress(VK_F6);
                        // Keep the crucial Sleep(1) as you mentioned
                        Sleep(1); //CRUCIAL YOU CANT SPAM F6 because targetId is getting weird!!! even if no monster
                        lock (memoryLock)
                        {
                            currentTargetId = targetId;
                        }
                        if (currentTargetId != 0)
                        {
                            Console.WriteLine(
                                "[DEBUG] Target found after movement, switching to combat"
                            );
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in path playback: {ex.Message}");
                Sleep(1);
            }
        }
        Console.WriteLine("Path playback ended");
    }
    static int FindClosestWaypointIndex(
        List<Coordinate> waypoints,
        int currentX,
        int currentY,
        int currentZ
    )
    {
        int closestIndex = 0;
        int minDistance = int.MaxValue;
        for (int i = 0; i < waypoints.Count; i++)
        {
            var waypoint = waypoints[i];
            int distance = Math.Abs(waypoint.X - currentX) + Math.Abs(waypoint.Y - currentY);
            if (distance < minDistance)
            {
                minDistance = distance;
                closestIndex = i;
            }
        }
        return closestIndex;
    }

    // Add these variables at the top of the class with other settings
    static bool waypointRandomizationEnabled = true; // Flag to enable/disable waypoint randomization
    static int randomizationRange = 1; // Maximum squares to randomize in each direction

    // Then return the result (which might be randomized)

    static Coordinate FindNextWaypoint(
    ref List<Coordinate> waypoints,
    int currentX,
    int currentY,
    int currentZ,
    ref int currentIndex
)
    {
        Console.WriteLine("\n=== FIND NEXT WAYPOINT DEBUG ===");

        // First, check if we should return from chase
        Coordinate returnPosition = chaseTracker.GetReturnPosition();
        if (chaseTracker.ShouldReturnToStart())
        {
            // Check if we're already close to the nearest waypoint
            int closestIndex = FindClosestWaypointIndex(waypoints, currentX, currentY, currentZ);
            Coordinate closestWaypoint = waypoints[closestIndex];
            int distanceToWaypointX = Math.Abs(closestWaypoint.X - currentX);
            int distanceToWaypointY = Math.Abs(closestWaypoint.Y - currentY);

            // If we're already close to a waypoint, reset chase state and continue normal pathing
            if (distanceToWaypointX < 5 && distanceToWaypointY < 5)
            {
                Console.WriteLine($"[DEBUG] Already close to waypoint (dx={distanceToWaypointX}, dy={distanceToWaypointY}), resetting chase state");
                chaseTracker.CompleteReturn();
                isReturningFromChase = false;

                // Continue with normal waypoint selection below
            }
            else if (returnPosition != null)
            {
                Console.WriteLine($"[DEBUG] Returning to chase start position: X={returnPosition.X}, Y={returnPosition.Y}, Z={returnPosition.Z}");

                // Check if we're already close to the return position
                int distanceX = Math.Abs(returnPosition.X - currentX);
                int distanceY = Math.Abs(returnPosition.Y - currentY);

                if (distanceX <= 1 && distanceY <= 1)
                {
                    Console.WriteLine("[DEBUG] Reached chase start position, resuming normal pathing");
                    chaseTracker.CompleteReturn();
                    isReturningFromChase = false;
                }
                else
                {
                    // We're still returning to start position
                    isReturningFromChase = true;
                    return returnPosition;
                }
            }
            else
            {
                // Something went wrong, reset the chase state
                Console.WriteLine("[DEBUG] Chase return position is null, resetting chase state");
                chaseTracker.CompleteReturn();
                isReturningFromChase = false;
            }
        }

        Console.WriteLine($"Current position: X={currentX}, Y={currentY}, Z={currentZ}");
        Console.WriteLine($"Current index: {currentIndex}");
        Console.WriteLine($"Total waypoints: {waypoints.Count}");

        if (waypoints.Count == 0)
        {
            Console.WriteLine("ERROR: Empty waypoints list");
            return new Coordinate { X = currentX, Y = currentY, Z = currentZ };
        }

        if (currentIndex < 0 || currentIndex >= waypoints.Count)
        {
            Console.WriteLine($"ERROR: Current index {currentIndex} out of bounds");
            currentIndex = Math.Max(0, Math.Min(waypoints.Count - 1, currentIndex));
            Console.WriteLine($"Corrected to index {currentIndex}");
        }

        // Check if we're at the last waypoint
        if (currentIndex == waypoints.Count - 1)
        {
            Console.WriteLine("At last waypoint, checking distance to first waypoint...");
            int distanceToFirst =
                Math.Abs(waypoints[0].X - currentX) + Math.Abs(waypoints[0].Y - currentY);
            Console.WriteLine($"Distance to first waypoint: {distanceToFirst} steps");

            if (distanceToFirst > 12)
            {
                Console.WriteLine("Distance > 12, REVERSING THE LIST NOW");
                waypoints.Reverse();
                currentIndex = 0;
                Console.WriteLine("List reversed, now at index 0");
            }
            else
            {
                Console.WriteLine("Distance <= 12, RESETTING to beginning of list");
                currentIndex = 0;
                Console.WriteLine("Reset to index 0");
            }
        }

        int maxSearchCount = 10;
        int maxAllowedX = 5;
        int maxAllowedY = 5;
        int bestIndex = -1;
        double maxDistance = 0;
        Console.WriteLine("Starting waypoint search...");

        int startIndex = currentIndex + 1;
        // If we've wrapped around to the beginning
        if (startIndex >= waypoints.Count)
        {
            startIndex = 0;
        }

        int endIndex = Math.Min(waypoints.Count - 1, startIndex + maxSearchCount - 1);
        Console.WriteLine($"Searching from index {startIndex} to {endIndex}");

        for (int index = startIndex; index <= endIndex; index++)
        {
            if (index < 0 || index >= waypoints.Count)
                continue;

            Coordinate waypoint = waypoints[index];
            int deltaX = Math.Abs(waypoint.X - currentX);
            int deltaY = Math.Abs(waypoint.Y - currentY);
            Console.WriteLine(
                $"  Checking waypoint[{index}]: X={waypoint.X}, Y={waypoint.Y}, deltaX={deltaX}, deltaY={deltaY}"
            );

            if (deltaX <= maxAllowedX && deltaY <= maxAllowedY)
            {
                double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                Console.WriteLine($"  Distance: {distance:F2} (within limits)");

                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    bestIndex = index;
                    Console.WriteLine($"  New best! Distance={distance:F2}, Index={bestIndex}");
                }
            }
            else
            {
                Console.WriteLine($"  Skipped: Outside distance limits");
            }
        }

        if (bestIndex == -1)
        {
            Console.WriteLine("No valid waypoint found ahead. Choosing adjacent waypoint...");

            // If we're at the last waypoint, we've already reset the index above
            if (currentIndex < waypoints.Count - 1)
            {
                bestIndex = currentIndex + 1;
                Console.WriteLine($"Moving to next waypoint (index {bestIndex})");
            }
            else
            {
                bestIndex = 0; // Reset to beginning 
                Console.WriteLine("At last waypoint, resetting to index 0");
            }
        }

        bestIndex = Math.Max(0, Math.Min(waypoints.Count - 1, bestIndex));
        currentIndex = bestIndex;
        Coordinate result = waypoints[bestIndex];

        // Apply randomization to the waypoint if enabled
        if (waypointRandomizationEnabled)
        {
            Coordinate originalResult = result;

            // Add random variation between -randomizationRange and +randomizationRange
            int randomX = result.X + random.Next(-randomizationRange, randomizationRange + 1);
            int randomY = result.Y + random.Next(-randomizationRange, randomizationRange + 1);

            int deltaX = Math.Abs(randomX - currentX);
            int deltaY = Math.Abs(randomY - currentY);

            if (deltaX <= 5 && deltaY <= 5)
            {
                result = new Coordinate
                {
                    X = randomX,
                    Y = randomY,
                    Z = result.Z // Keep the same Z coordinate (level)
                };

                Console.WriteLine($"  RANDOMIZED WAYPOINT: Original({originalResult.X},{originalResult.Y}) → Random({result.X},{result.Y})");
            }
            else
            {
                Console.WriteLine($"  RANDOMIZATION REJECTED: Random({randomX},{randomY}) exceeds 5-unit limit from current position({currentX},{currentY})");
                // Keep the original result
            }

            result = new Coordinate
            {
                X = randomX,
                Y = randomY,
                Z = result.Z // Keep the same Z coordinate (level)
            };

            

            Console.WriteLine($"  RANDOMIZED WAYPOINT: Original({originalResult.X},{originalResult.Y}) → Random({result.X},{result.Y})");
        }

        Console.WriteLine(
            $"Final choice: waypoint[{bestIndex}]: X={result.X}, Y={result.Y}, Z={result.Z}"
        );
        Console.WriteLine("=== END FIND NEXT WAYPOINT DEBUG ===\n");
        return result;
    }
    static bool smallWindow = true;
    static bool previousSmallWindowValue = true; // Track the previous state

    static int pixelSize = smallWindow ? 38 : 58;
    static int baseYOffset = smallWindow ? 260 : 300;
    static int inventoryX = smallWindow ? 620 : 940;
    static int inventoryY = 65;
    static int equipmentX = smallWindow ? 800 : 1115;
    static int equipmentY = 150;
    static int secondSlotBpX = smallWindow ? 850 : 1165;
    static int secondSLotBpY = 250;
    static (int, int)[] normalCoordinates = new (int, int)[]
    {
        (1126, 325),
        (1165, 325),
        (1203, 325),
        (1236, 325),
        (1126, 340),
        (1165, 340),
        (1203, 340),
        (1236, 340)
    };

    static (int, int)[] smallCoordinates = new (int, int)[]
    {
        (800, 340),  
        (800+pixelSize, 340),
        (800+2*pixelSize, 340),
        (800+3*pixelSize, 340),
        (800, 380),
        (800+pixelSize, 380),
        (800+2*pixelSize, 380),
        (800+3*pixelSize, 380)
    };
    static (int, int)[] GetCorspeFoodCoordinates()
    {
        return smallWindow ? smallCoordinates : normalCoordinates;
    }

    static void UpdateUIPositions()
    {
        GetClientRect(targetWindow, out RECT windowRect);
        int windowHeight = windowRect.Bottom - windowRect.Top;
        Console.WriteLine($"[DEBUG] Detected window height: {windowHeight}px");
        smallWindow = windowHeight < 1200;
        Console.WriteLine($"[DEBUG] Using {(smallWindow ? "small window (1080p)" : "large window (1440p)")} settings");

        bool valueChanged = (previousSmallWindowValue != smallWindow);
        previousSmallWindowValue = smallWindow;

        pixelSize = smallWindow ? 38 : 58;
        baseYOffset = smallWindow ? 260 : 300;
        inventoryX = smallWindow ? 620 : 940;
        inventoryY = 65;
        equipmentX = smallWindow ? 800 : 1115;
        equipmentY = 150;
        secondSlotBpX = smallWindow ? 850 : 1165;
        secondSLotBpY = 250;

        {
            Console.WriteLine($"[DEBUG] Window size changed to: {(smallWindow ? "small (1080p)" : "large (1440p)")}");
            Console.WriteLine($"[DEBUG] UI positions updated:");
            Console.WriteLine($"  - Pixel size: {pixelSize}");
            Console.WriteLine($"  - Base Y Offset: {baseYOffset}");
            Console.WriteLine($"  - Inventory position: ({inventoryX},{inventoryY})");
            Console.WriteLine($"  - Equipment position: ({equipmentX},{equipmentY})");
            Console.WriteLine($"  - Second slot position: ({secondSlotBpX},{secondSLotBpY})");
        }
    }

    static bool ClickWaypoint(Coordinate target)
    {
        try
        {
            bool isChaseReturnPoint = chaseTracker.ShouldReturnToStart() &&
                                 target.X == chaseTracker.GetReturnPosition().X &&
                                 target.Y == chaseTracker.GetReturnPosition().Y;

            if (isChaseReturnPoint)
            {
                Console.WriteLine("[DEBUG] Clicking waypoint that is a chase return position");
            }

            
            int currentX,
                currentY;
            lock (memoryLock)
            {
                currentX = posX;
                currentY = posY;
            }
            GetClientRect(targetWindow, out RECT rect);
            int baseX = (rect.Right - rect.Left) / 2 - 186;
            int baseY = (rect.Bottom - rect.Top) / 2 - baseYOffset;

            //POINT screenPoint = new POINT { X = baseX, Y = baseY };
            //ClientToScreen(targetWindow, ref screenPoint);
           // SetCursorPos(screenPoint.X, screenPoint.Y);

            int diffX = target.X - currentX;
            int diffY = target.Y - currentY;
            int targetX = baseX + (diffX * pixelSize);
            int targetY = baseY + (diffY * pixelSize);
            if (GetTargetId() != 0)
            {
                Console.WriteLine("[DEBUG] Combat detected, canceling movement");
                return false;
            }
            int lParam = (targetY << 16) | (targetX & 0xFFFF);
            SendKeyPress(VK_ESCAPE);
            Sleep(64);
            PostMessage(targetWindow, 0x0200, IntPtr.Zero, (IntPtr)lParam);
            Sleep(64);
            PostMessage(targetWindow, WM_LBUTTONDOWN, (IntPtr)1, (IntPtr)lParam);
            Sleep(64);
            PostMessage(targetWindow, WM_LBUTTONUP, IntPtr.Zero, (IntPtr)lParam);
            Sleep(64);
            int centerLParam = (baseY << 16) | (baseX & 0xFFFF);
            //PostMessage(targetWindow, 0x0200, IntPtr.Zero, (IntPtr)centerLParam);
            Console.WriteLine(
                $"[DEBUG] Clicked: X={diffX}, Y={diffY} (Screen: {targetX}, {targetY}, Pixel Size: {pixelSize})"
            );
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Click Error: {ex.Message}");
            return false;
        }
    }
    const byte VK_ESCAPE = 0x1B;
    const int VK_F6 = 0x75;
    const int WM_LBUTTONDOWN = 0x0201;
    const int WM_LBUTTONUP = 0x0202;
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion u;
    }
    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
    const int INPUT_MOUSE = 0;
    const uint MOUSEEVENTF_MOVE = 0x0001;
    const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")]
    static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left,
            Top,
            Right,
            Bottom;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }
    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);
    static void CloseCorspe(IntPtr hWnd)
    {
        (int x, int y)[] locations = new (int, int)[] { (1262, 320) };
        GetClientRect(hWnd, out RECT rect);
        if (rect.Right < 1237 || rect.Bottom < 319)
        {
            Console.WriteLine(
                $"Warning: Window size ({rect.Right}x{rect.Bottom}) may be too small for target coordinates"
            );
        }
        foreach (var location in locations)
        {
            int x = location.x;
            int y = location.y;
            POINT screenPoint = new POINT { X = x, Y = y };
            ClientToScreen(hWnd, ref screenPoint);
            Sleep(1);
            VirtualLeftClick(hWnd, x, y);
            Sleep(1);
        }
    }
    static void CorpseEatFood(IntPtr hWnd)
    {
        (int x, int y)[] locations = GetCorspeFoodCoordinates();
        GetClientRect(hWnd, out RECT rect);
        if (rect.Right < 1237 || rect.Bottom < 319)
        {
            Console.WriteLine(
                $"Warning: Window size ({rect.Right}x{rect.Bottom}) may be too small for target coordinates"
            );
        }
        foreach (var location in locations)
        {
            int x = location.x;
            int y = location.y;
            POINT screenPoint = new POINT { X = x, Y = y };
            ClientToScreen(hWnd, ref screenPoint);
            Sleep(1);
            VirtualRightClick(hWnd, x, y);
            Sleep(1);
        }
    }
    static void ClickSecondSlotInBackpack(IntPtr hWnd)
    {
        (int x, int y)[] locations = new (int, int)[] { (secondSlotBpX, secondSLotBpY) };

        GetClientRect(hWnd, out RECT rect);
        if (rect.Right < 1237 || rect.Bottom < 319)
        {
            Console.WriteLine(
                $"Warning: Window size ({rect.Right}x{rect.Bottom}) may be too small for target coordinates"
            );
        }
        foreach (var location in locations)
        {
            int x = location.x;
            int y = location.y;
            POINT screenPoint = new POINT { X = x, Y = y };
            ClientToScreen(hWnd, ref screenPoint);
            Sleep(1);
            //SetCursorPos(screenPoint.X, screenPoint.Y);
            VirtualRightClick(hWnd, x, y);
            Sleep(1);
            int currentTargetId = GetTargetId();
            if (currentTargetId != 0)
            {
                Console.WriteLine(
                    $"Target acquired after clicking at ({x}, {y}). Target ID: {currentTargetId}"
                );
            }
        }
    }
    static void DragItemToCharacterCenter(IntPtr hWnd)
    {
        int sourceX = 1236;
        int sourceY = 285;
        GetClientRect(hWnd, out RECT rect);
        int centerX = (rect.Right - rect.Left) / 2 - 186;
        int centerY = (rect.Bottom - rect.Top) / 2 - 300;
        Console.WriteLine($"Moving to source position ({sourceX}, {sourceY})");
        IntPtr sourceLParam = MakeLParam(sourceX, sourceY);
        SendMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, sourceLParam);
        Sleep(1);
        SendMessage(hWnd, WM_LBUTTONDOWN, IntPtr.Zero, sourceLParam);
        Sleep(1);
        IntPtr centerLParam = MakeLParam(centerX, centerY);
        SendMessage(hWnd, WM_MOUSEMOVE, new IntPtr(MK_LBUTTON), centerLParam);
        Sleep(1);
        SendMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, centerLParam);
        Sleep(1);
        Console.WriteLine($"Completed drag from ({sourceX}, {sourceY}) to ({centerX}, {centerY})");
    }
    const int MK_LBUTTON = 0x0001;
    static IntPtr MakeLParam(int x, int y)
    {
        return (IntPtr)((y << 16) | (x & 0xFFFF));
    }
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    static void ShuffleArray((int dx, int dy)[] array, Random random)
    {
        int n = array.Length;
        for (int i = n - 1; i > 0; i--)
        {
            // Pick a random index from 0 to i
            int j = random.Next(0, i + 1);

            // Swap array[i] with array[j]
            (array[i], array[j]) = (array[j], array[i]);
        }
    }
    static void ClickAroundCharacter(IntPtr hWnd)
    {
        ClickSecondSlotInBackpack(hWnd);
        (int dx, int dy)[] directions = new (int, int)[]
        {
            (0, -1),
            (1, -1),
            (1, 0),
            (1, 1),
            (0, 1),
            (-1, 1),
            (-1, 0),
            (-1, -1)
        };

        Random random = new Random();
        ShuffleArray(directions, random);

        GetClientRect(hWnd, out RECT rect);
        int centerX = (rect.Right - rect.Left) / 2 - 186;
        int centerY = (rect.Bottom - rect.Top) / 2 - baseYOffset;
        foreach (var direction in directions)
        {
            int dx = direction.dx;
            int dy = direction.dy;
            int clickX = centerX + (int)(dx * pixelSize);
            int clickY = centerY + (int)(dy * pixelSize);
            VirtualRightClick(targetWindow, clickX, clickY);
            int currentTargetId = GetTargetId();
            if (currentTargetId != 0)
            {
                Console.WriteLine(
                    $"Target acquired after clicking at ({clickX}, {clickY}). Target ID: {currentTargetId}"
                );
            }
        }
        //CorpseEatFood(targetWindow);
        //CloseCorspe(targetWindow);
    }
    const int WM_MOUSEMOVE = 0x0200;
    const int WM_RBUTTONDOWN = 0x0204;
    const int WM_RBUTTONUP = 0x0205;
    const uint VK_LCONTROL = 0xA2;
    const uint VK_LMENU = 0xA4;
    const uint KEYEVENTF_KEYUP = 0x0002;
    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);
    static void VirtualRightClick(IntPtr hWnd, int x, int y)
    {
        int lParam = (y << 16) | (x & 0xFFFF);
        SendMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, (IntPtr)lParam);
        SendMessage(hWnd, WM_RBUTTONDOWN, (IntPtr)1, (IntPtr)lParam);
        SendMessage(hWnd, WM_RBUTTONUP, IntPtr.Zero, (IntPtr)lParam);
    }
    static void VirtualLeftClick(IntPtr hWnd, int x, int y)
    {
        int lParam = (y << 16) | (x & 0xFFFF);
        PostMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, (IntPtr)lParam);
        Sleep(1);
        PostMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)1, (IntPtr)lParam);
        Sleep(1);
        PostMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, (IntPtr)lParam);
        Sleep(1);
    }
    static int GetTargetId()
    {
        lock (memoryLock)
        {
            return targetId;
        }
    }
    static (int monsterX, int monsterY, int monsterZ) GetTargetMonsterCoordinates()
    {
        int targetId = 0;
        int monsterX = 0,
            monsterY = 0,
            monsterZ = 0;
        lock (memoryLock)
        {
            targetId = ReadInt32(processHandle, moduleBase, targetIdOffset);
            if (targetId != 0)
            {
                IntPtr monsterInstancePtr = IntPtr.Zero;
                byte[] buffer = new byte[4];
                if (
                    ReadProcessMemory(
                        processHandle,
                        IntPtr.Add(moduleBase, (int)targetIdOffset),
                        buffer,
                        buffer.Length,
                        out _
                    )
                )
                {
                    monsterInstancePtr = (IntPtr)BitConverter.ToInt32(buffer, 0);
                    if (
                        ReadProcessMemory(
                            processHandle,
                            IntPtr.Add(monsterInstancePtr, 0x000C),
                            buffer,
                            buffer.Length,
                            out _
                        )
                    )
                    {
                        monsterX = BitConverter.ToInt32(buffer, 0);
                    }
                    if (
                        ReadProcessMemory(
                            processHandle,
                            IntPtr.Add(monsterInstancePtr, 0x0010),
                            buffer,
                            buffer.Length,
                            out _
                        )
                    )
                    {
                        monsterY = BitConverter.ToInt32(buffer, 0);
                    }
                    if (
                        ReadProcessMemory(
                            processHandle,
                            IntPtr.Add(monsterInstancePtr, 0x0014),
                            buffer,
                            buffer.Length,
                            out _
                        )
                    )
                    {
                        monsterZ = BitConverter.ToInt32(buffer, 0);
                    }
                }
            }
        }
        return (monsterX, monsterY, monsterZ);
    }
    static string ReadStringFromMemory(IntPtr handle, IntPtr address, int maxLength = 128)
    {
        try
        {
            byte[] buffer = new byte[maxLength];
            int bytesRead;
            if (!ReadProcessMemory(handle, address, buffer, buffer.Length, out bytesRead))
            {
                return string.Empty;
            }
            int nullTerminatorPos = 0;
            while (nullTerminatorPos < buffer.Length && buffer[nullTerminatorPos] != 0)
            {
                nullTerminatorPos++;
            }
            if (nullTerminatorPos > 0)
            {
                return Encoding.ASCII.GetString(buffer, 0, nullTerminatorPos);
            }
            return string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading string from memory: {ex.Message}");
            return string.Empty;
        }
    }
    static List<string> blacklistedMonsterNames = new List<string> { "Rat", "Rabbit", "Snake" };
    static (int monsterX, int monsterY, int monsterZ, string monsterName) GetTargetMonsterInfo()
    {
        int targetId = 0;
        int monsterX = 0,
            monsterY = 0,
            monsterZ = 0;
        string monsterName = "";
        lock (memoryLock)
        {
            targetId = ReadInt32(processHandle, moduleBase, targetIdOffset);
            if (targetId != 0)
            {
                IntPtr monsterInstancePtr = IntPtr.Zero;
                byte[] buffer = new byte[4];
                if (
                    ReadProcessMemory(
                        processHandle,
                        IntPtr.Add(moduleBase, (int)targetIdOffset),
                        buffer,
                        buffer.Length,
                        out _
                    )
                )
                {
                    monsterInstancePtr = (IntPtr)BitConverter.ToInt32(buffer, 0);
                    if (
                        ReadProcessMemory(
                            processHandle,
                            IntPtr.Add(monsterInstancePtr, 0x000C),
                            buffer,
                            buffer.Length,
                            out _
                        )
                    )
                    {
                        monsterX = BitConverter.ToInt32(buffer, 0);
                    }
                    if (
                        ReadProcessMemory(
                            processHandle,
                            IntPtr.Add(monsterInstancePtr, 0x0010),
                            buffer,
                            buffer.Length,
                            out _
                        )
                    )
                    {
                        monsterY = BitConverter.ToInt32(buffer, 0);
                    }
                    if (
                        ReadProcessMemory(
                            processHandle,
                            IntPtr.Add(monsterInstancePtr, 0x0014),
                            buffer,
                            buffer.Length,
                            out _
                        )
                    )
                    {
                        monsterZ = BitConverter.ToInt32(buffer, 0);
                    }
                    monsterName = ReadStringFromMemory(
                        processHandle,
                        IntPtr.Add(monsterInstancePtr, 0x0030)
                    );
                    if (targetId == 0)
                    {
                        monsterName = "";
                    }
                }
            }
        }
        return (monsterX, monsterY, monsterZ, monsterName);
    }
    static int lastRingEquippedTargetId = 0;
    static bool isRingCurrentlyEquipped = false;
    static List<string> blacklistedRingMonsters = new List<string> { "Poison Spider", };
    static void ToggleRing(IntPtr hWnd, bool equip)
    {
       // return;
        try
        {
            int currentTargetId;
            lock (memoryLock)
            {
                currentTargetId = targetId;
            }
            if (equip && isRingCurrentlyEquipped && currentTargetId == lastRingEquippedTargetId)
            {
                return;
            }
            if (equip && currentTargetId != 0)
            {
                var (monsterX, monsterY, monsterZ, monsterName) = GetTargetMonsterInfo();
                if (
                    !string.IsNullOrEmpty(monsterName)
                    && blacklistedRingMonsters.Contains(monsterName)
                )
                {

                    Console.WriteLine(
                        $"[DEBUG] Monster '{monsterName}' is blacklisted for ring usage"
                    );
                    return;
                }
                int playerX,
                    playerY,
                    playerZ;
                lock (memoryLock)
                {
                    playerX = posX;
                    playerY = posY;
                    playerZ = posZ;
                }
                double distance = Math.Sqrt(
                    Math.Pow(monsterX - playerX, 2) + Math.Pow(monsterY - playerY, 2)
                );
                if (distance > 2.5)
                {
                    return;
                }
            }

            int sourceX = equip ? inventoryX : equipmentX;
            int sourceY = equip ? inventoryY : equipmentY;
            int destX = equip ? equipmentX : inventoryX;
            int destY = equip ? equipmentY : inventoryY;
            Console.WriteLine($"[DEBUG] {(equip ? "Equipping" : "De-equipping")} ring");
            IntPtr sourceLParam = MakeLParam(sourceX, sourceY);
            PostMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, sourceLParam);
            Sleep(1);
            PostMessage(hWnd, WM_LBUTTONDOWN, IntPtr.Zero, sourceLParam);
            Sleep(1);
            IntPtr destLParam = MakeLParam(destX, destY);
            PostMessage(hWnd, WM_MOUSEMOVE, new IntPtr(MK_LBUTTON), destLParam);
            Sleep(1);
            PostMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, destLParam);
            Sleep(1);
            if (equip)
            {
                lastRingEquippedTargetId = currentTargetId;
                isRingCurrentlyEquipped = true;
                Console.WriteLine(
                    $"[DEBUG] Successfully equipped ring for target {currentTargetId}"
                );
            }
            else
            {
                isRingCurrentlyEquipped = false;
                Console.WriteLine($"[DEBUG] Successfully de-equipped ring");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error toggling ring: {ex.Message}");
        }
    }

    static void Sleep(int miliseconds)
    {
        Thread.Sleep(miliseconds);
    }

    static CancellationTokenSource positionAlertCts = null;
    static readonly object soundLock = new object();
    static DateTime lastPositionAlertTime = DateTime.MinValue;
    static readonly int POSITION_ALERT_COOLDOWN_SEC = 60; // One minute cooldown for position alerts
    static readonly int MAX_DISTANCE_SQMS = 50; // Maximum allowed distance in sqms

    // Add these Win32 API declarations for playing sounds
    [DllImport("kernel32.dll")]
    static extern bool Beep(int frequency, int duration);

    // Alternative sound method if Beep doesn't work well enough
    [DllImport("winmm.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

    // Sound flags for PlaySound API
    const uint SND_FILENAME = 0x00020000;
    const uint SND_ASYNC = 0x0001;
    const uint SND_NODEFAULT = 0x0002;

    // Sound frequencies and durations
    const int CLICK_COMPLETED_FREQ = 800;
    const int CLICK_COMPLETED_DURATION = 200;
    const int POSITION_ALERT_FREQ = 1200;
    const int POSITION_ALERT_DURATION = 500;

    // Add this method to the Program class to check if sound files exist
    static void InitializeSounds()
    {
        try
        {
            // Create sound for click around completion
            string clickCompletedSoundPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "click_completed.wav"
            );

            // Create sound for position alert
            string positionAlertSoundPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "position_alert.wav"
            );

            // Check if sound files exist
            if (!File.Exists(clickCompletedSoundPath))
            {
                Console.WriteLine("Warning: click_completed.wav not found. Using system beep instead.");
            }
            else
            {
                Console.WriteLine("Found click_completed.wav");
            }

            if (!File.Exists(positionAlertSoundPath))
            {
                Console.WriteLine("Warning: position_alert.wav not found. Using system beep instead.");
            }
            else
            {
                Console.WriteLine("Found position_alert.wav");
            }

            Console.WriteLine("Sound system initialized successfully.");

            // Test beep to verify sound capability
            //Beep(800, 200);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing sounds: {ex.Message}");
        }
    }

    // Add this method to play the click completed sound
    static void PlayClickCompletedSound()
    {
        try
        {
            string clickCompletedSoundPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "click_completed.wav"
            );

            if (File.Exists(clickCompletedSoundPath))
            {
                // Play WAV file if it exists
                bool success = PlaySound(clickCompletedSoundPath, IntPtr.Zero, SND_FILENAME | SND_ASYNC);
                if (success)
                {
                    Console.WriteLine("[SOUND] Click around completed sound played from file.");
                    return;
                }
            }

            // Fall back to Beep if file doesn't exist or PlaySound failed
            Beep(CLICK_COMPLETED_FREQ, CLICK_COMPLETED_DURATION);
            Console.WriteLine("[SOUND] Click around completed beep played.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error playing click completed sound: {ex.Message}");
            try
            {
                // Last resort fallback
                Console.Beep(CLICK_COMPLETED_FREQ, CLICK_COMPLETED_DURATION);
            }
            catch
            {
                Console.WriteLine("All sound methods failed!");
            }
        }
    }

    // Add this method to start playing the position alert sound repeatedly
    static void StartPositionAlertSound(int maxDurationSeconds = 60)
    {
        lock (soundLock)
        {
            // Check if we're already playing or if we're in cooldown period
            if (positionAlertCts != null ||
                (DateTime.Now - lastPositionAlertTime).TotalSeconds < POSITION_ALERT_COOLDOWN_SEC)
            {
                return;
            }

            Console.WriteLine($"[SOUND] Starting position alert sound (max {maxDurationSeconds} seconds).");
            lastPositionAlertTime = DateTime.Now;

            // Create a new cancellation token source
            positionAlertCts = new CancellationTokenSource();
            var token = positionAlertCts.Token;

            // Get the path to position alert sound file
            string positionAlertSoundPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "position_alert.wav"
            );
            bool useWavFile = File.Exists(positionAlertSoundPath);

            // Start a task to play the sound repeatedly
            Task.Run(() =>
            {
                try
                {
                    DateTime startTime = DateTime.Now;
                    while (!token.IsCancellationRequested &&
                           (DateTime.Now - startTime).TotalSeconds < maxDurationSeconds)
                    {
                        try
                        {
                            if (useWavFile)
                            {
                                // Try to play the WAV file
                                bool success = PlaySound(positionAlertSoundPath, IntPtr.Zero, SND_FILENAME | SND_ASYNC);
                                if (!success)
                                {
                                    // Fall back to Beep if PlaySound failed
                                    Beep(POSITION_ALERT_FREQ, POSITION_ALERT_DURATION);
                                }
                            }
                            else
                            {
                                // Use Beep if no WAV file
                                Beep(POSITION_ALERT_FREQ, POSITION_ALERT_DURATION);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error playing position alert sound: {ex.Message}");
                            try
                            {
                                // Last resort fallback
                                Console.Beep(POSITION_ALERT_FREQ, POSITION_ALERT_DURATION);
                            }
                            catch
                            {
                                Console.WriteLine("All sound methods failed!");
                            }
                        }

                        // Wait before playing again
                        Thread.Sleep(1500);
                    }

                    Console.WriteLine("[SOUND] Position alert sound stopped.");
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("[SOUND] Position alert sound cancelled.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in position alert sound loop: {ex.Message}");
                }
                finally
                {
                    lock (soundLock)
                    {
                        positionAlertCts = null;
                    }
                }
            }, token);
        }
    }

    // Add this method to stop the position alert sound
    static void StopPositionAlertSound()
    {
        lock (soundLock)
        {
            if (positionAlertCts != null)
            {
                positionAlertCts.Cancel();
                positionAlertCts = null;
                Console.WriteLine("[SOUND] Position alert sound cancelled.");
            }
        }
    }

    // Add this method to check if position is too far from any waypoint
    static void CheckPositionDistance(List<Coordinate> waypoints)
    {
        if (waypoints == null || waypoints.Count == 0)
        {
            return;
        }

        int currentX, currentY, currentZ;
        lock (memoryLock)
        {
            currentX = posX;
            currentY = posY;
            currentZ = posZ;
        }

        // Find the closest waypoint
        int minDistance = int.MaxValue;
        foreach (var waypoint in waypoints)
        {
            int distance = Math.Abs(waypoint.X - currentX) + Math.Abs(waypoint.Y - currentY);
            if (distance < minDistance)
            {
                minDistance = distance;
            }
        }

        // If the closest waypoint is too far, start playing the alert
        if (minDistance > MAX_DISTANCE_SQMS)
        {
            Console.WriteLine($"[WARNING] Position too far from any waypoint! Distance: {minDistance} sqm");
            StartPositionAlertSound();
        }
        else
        {
            //StopPositionAlertSound();
        }
    }

    class ChasePathTracker
    {
        private bool isChasing = false;
        private Coordinate chaseStartPosition = null;
        private List<Coordinate> chasePath = new List<Coordinate>();
        private int lastTargetId = 0;
        private bool needToReturnToStart = false;
        private object chaseLock = new object();
        private static List<Coordinate> waypoints = null; // Store reference to waypoints

        // Fields for delayed display
        private Dictionary<string, DateTime> messageTypeTimes = new Dictionary<string, DateTime>();
        private const double DISPLAY_COOLDOWN_SECONDS = 1.5;

        // Message categories
        private const string MSG_CHASE_START = "CHASE_START";
        private const string MSG_TARGET_CHANGE = "TARGET_CHANGE";
        private const string MSG_DISTANCE_EXCEED = "DISTANCE_EXCEED";
        private const string MSG_DISTANCE_OK = "DISTANCE_OK";
        private const string MSG_WAYPOINT_INFO = "WAYPOINT_INFO";
        private const string MSG_NO_WAYPOINTS = "NO_WAYPOINTS";

        public void Update(int currentX, int currentY, int currentZ, int targetId)
        {
            lock (chaseLock)
            {
                // Start of chase detection (was not chasing, now is chasing)
                if (!isChasing && targetId != 0)
                {
                    StartChase(currentX, currentY, currentZ, targetId);
                }
                // During chase, record path
                else if (isChasing && targetId != 0)
                {
                    // Record position if we moved
                    RecordPosition(currentX, currentY, currentZ);

                    // Handle if target monster changed during chase
                    if (targetId != lastTargetId)
                    {
                        string message = $"[DEBUG] Target changed during chase from {lastTargetId} to {targetId}";
                        DisplayWithCooldown(MSG_TARGET_CHANGE, message);
                        lastTargetId = targetId;
                    }
                }
                // End of chase detection (was chasing, now killed monster)
                else if (isChasing && targetId == 0 && lastTargetId != 0)
                {
                    // Get the waypoints from loadedCoords if available
                    if (Program.loadedCoords != null && Program.loadedCoords.cords.Count > 0)
                    {
                        waypoints = Program.loadedCoords.cords;
                        EndChase(currentX, currentY, currentZ, waypoints);
                    }
                    else
                    {
                        // Fallback if waypoints not available
                        EndChaseSimple(currentX, currentY, currentZ);
                    }
                }
            }
        }

        private void StartChase(int x, int y, int z, int targetId)
        {
            string message = $"[DEBUG] Starting chase of monster ID: {targetId}";
            DisplayWithCooldown(MSG_CHASE_START, message);

            isChasing = true;
            lastTargetId = targetId;
            chaseStartPosition = new Coordinate { X = x, Y = y, Z = z };
            chasePath.Clear();
            // Add starting position to path
            chasePath.Add(new Coordinate { X = x, Y = y, Z = z });
        }

        private void RecordPosition(int x, int y, int z)
        {
            // Only add to path if position changed
            if (chasePath.Count == 0 ||
                chasePath[chasePath.Count - 1].X != x ||
                chasePath[chasePath.Count - 1].Y != y ||
                chasePath[chasePath.Count - 1].Z != z)
            {
                chasePath.Add(new Coordinate { X = x, Y = y, Z = z });

                // Optional: Limit path length to prevent memory issues
                const int MAX_PATH_LENGTH = 1000;
                if (chasePath.Count > MAX_PATH_LENGTH)
                {
                    chasePath.RemoveAt(0);
                }
            }
        }

        // Original simple method as fallback
        private void EndChaseSimple(int currentX, int currentY, int currentZ)
        {
            // Check if we're far from the start position
            int distanceX = Math.Abs(currentX - chaseStartPosition.X);
            int distanceY = Math.Abs(currentY - chaseStartPosition.Y);

            if (distanceX > 5 || distanceY > 5)
            {
                string message = $"[DEBUG] Distance threshold exceeded (X:{distanceX}, Y:{distanceY}), need to return to start position";
                DisplayWithCooldown(MSG_DISTANCE_EXCEED, message);
                needToReturnToStart = true;
            }
            else
            {
                string message = $"[DEBUG] Distance within threshold (X:{distanceX}, Y:{distanceY}), no need to return";
                DisplayWithCooldown(MSG_DISTANCE_OK, message);
                // Reset chase state without returning
                ResetChaseState();
            }
        }

        // New method that checks against closest waypoint
        private void EndChase(int currentX, int currentY, int currentZ, List<Coordinate> waypoints)
        {
            // Find the closest waypoint to current position
            int closestIndex = -1;
            int minDistance = int.MaxValue;

            for (int i = 0; i < waypoints.Count; i++)
            {
                var waypoint = waypoints[i];
                int distance = Math.Abs(waypoint.X - currentX) + Math.Abs(waypoint.Y - currentY);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestIndex = i;
                }
            }

            // If closest waypoint is far away, need to return to chase start position
            int distanceX = 0;
            int distanceY = 0;

            if (closestIndex >= 0)
            {
                distanceX = Math.Abs(currentX - waypoints[closestIndex].X);
                distanceY = Math.Abs(currentY - waypoints[closestIndex].Y);

                // Check if already close to a waypoint
                if (distanceX < 5 && distanceY < 5)
                {
                    string message = $"[DEBUG] Already close to waypoint (dx={distanceX}, dy={distanceY}), no need to return";
                    DisplayWithCooldown(MSG_DISTANCE_OK, message);
                    // Reset chase state without returning
                    ResetChaseState();
                    return;
                }

                // Otherwise, check against thresholds
                if (distanceX > 5 || distanceY > 5)
                {
                    string message1 = $"[DEBUG] Distance to closest waypoint exceeded threshold (X:{distanceX}, Y:{distanceY}), need to return to start position";
                    DisplayWithCooldown(MSG_DISTANCE_EXCEED, message1);

                    string message2 = $"[DEBUG] Current position: ({currentX},{currentY}), Closest waypoint: ({waypoints[closestIndex].X},{waypoints[closestIndex].Y})";
                    DisplayWithCooldown(MSG_WAYPOINT_INFO, message2);

                    needToReturnToStart = true;
                }
                else
                {
                    string message = $"[DEBUG] Distance to closest waypoint within threshold (X:{distanceX}, Y:{distanceY}), no need to return";
                    DisplayWithCooldown(MSG_DISTANCE_OK, message);
                    // Reset chase state without returning
                    ResetChaseState();
                }
            }
            else
            {
                // No waypoints available, use original logic with chase start position
                distanceX = Math.Abs(currentX - chaseStartPosition.X);
                distanceY = Math.Abs(currentY - chaseStartPosition.Y);

                if (distanceX > 5 || distanceY > 5)
                {
                    string message = $"[DEBUG] No waypoints found, distance to chase start exceeded threshold (X:{distanceX}, Y:{distanceY}), need to return to start position";
                    DisplayWithCooldown(MSG_NO_WAYPOINTS, message);
                    needToReturnToStart = true;
                }
                else
                {
                    string message = $"[DEBUG] No waypoints found, distance within threshold (X:{distanceX}, Y:{distanceY}), no need to return";
                    DisplayWithCooldown(MSG_NO_WAYPOINTS, message);
                    ResetChaseState();
                }
            }
        }

        public bool ShouldReturnToStart()
        {
            lock (chaseLock)
            {
                return needToReturnToStart;
            }
        }

        public Coordinate GetReturnPosition()
        {
            lock (chaseLock)
            {
                return chaseStartPosition;
            }
        }

        public void CompleteReturn()
        {
            lock (chaseLock)
            {
                ResetChaseState();
            }
        }

        private void ResetChaseState()
        {
            isChasing = false;
            needToReturnToStart = false;
            lastTargetId = 0;
            // Keep chaseStartPosition and chasePath for debugging if needed
        }

        public List<Coordinate> GetChasePath()
        {
            lock (chaseLock)
            {
                return new List<Coordinate>(chasePath);
            }
        }

        // Improved DisplayWithCooldown that uses message categories instead of actual messages
        private void DisplayWithCooldown(string messageType, string message)
        {
            DateTime now = DateTime.Now;

            // Check if this message type is on cooldown
            if (!messageTypeTimes.ContainsKey(messageType) ||
                (now - messageTypeTimes[messageType]).TotalSeconds >= DISPLAY_COOLDOWN_SECONDS)
            {
                // Display the message
                Console.WriteLine(message);

                // Update the last display time for this message type
                messageTypeTimes[messageType] = now;
            }
        }

        // Public method to display a message with a specific category
        public void DisplayMessage(string messageType, string message)
        {
            lock (chaseLock)
            {
                DisplayWithCooldown(messageType, message);
            }
        }
    }

    // Add these variables to the Program class
    static Dictionary<int, DateTime> blacklistedTargetTimers = new Dictionary<int, DateTime>();
    static DateTime lastPositionTime = DateTime.MinValue;
    static int lastPositionX = 0;
    static int lastPositionY = 0;
    static bool isPlayerStuck = false;
    static readonly TimeSpan TARGET_BLACKLIST_DURATION = TimeSpan.FromSeconds(30);
    static readonly int MAX_MONSTER_DISTANCE = 2; // Maximum allowed distance in sqm
    static readonly int STUCK_DETECTION_TIME_MS = 2300; // Time to consider player stuck

    // Add this method to check if a target ID is blacklisted
    static bool IsTargetBlacklisted(int targetId)
    {
        // If target ID is not in dictionary or time has expired, it's not blacklisted
        if (!blacklistedTargetTimers.ContainsKey(targetId))
            return false;

        if (DateTime.Now > blacklistedTargetTimers[targetId])
        {
            // Remove expired blacklisted target
            blacklistedTargetTimers.Remove(targetId);
            return false;
        }

        return true;
    }

    // Add this method to clean expired blacklisted targets
    static void CleanBlacklistedTargets()
    {
        var now = DateTime.Now;
        var expiredTargets = blacklistedTargetTimers
            .Where(kvp => now > kvp.Value)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var targetId in expiredTargets)
        {
            blacklistedTargetTimers.Remove(targetId);
        }

        if (expiredTargets.Count > 0)
        {
            Console.WriteLine($"[DEBUG] Removed {expiredTargets.Count} expired blacklisted targets");
        }
    }

    // Add this method to check for stuck player
    static void UpdatePlayerMovementStatus(int currentX, int currentY)
    {
        // If this is the first check or player has moved
        if (lastPositionTime == DateTime.MinValue ||
            lastPositionX != currentX ||
            lastPositionY != currentY)
        {
            lastPositionTime = DateTime.Now;
            lastPositionX = currentX;
            lastPositionY = currentY;
            isPlayerStuck = false;
        }
        else
        {
            // Player hasn't moved, check if stuck based on time
            TimeSpan stuckTime = DateTime.Now - lastPositionTime;
            isPlayerStuck = stuckTime.TotalMilliseconds >= STUCK_DETECTION_TIME_MS;
        }
    }

    // Add this method to check monster distance and blacklist if needed
    static bool CheckMonsterDistanceAndBlacklist()
    {
        int currentX, currentY, currentZ, currentTargetId;
        bool hasBlacklisted = false;

        lock (memoryLock)
        {
            currentX = posX;
            currentY = posY;
            currentZ = posZ;
            currentTargetId = targetId;
        }

        // Update movement status to detect if player is stuck
        UpdatePlayerMovementStatus(currentX, currentY);

        // Only check if we have a target and player appears stuck
        if (currentTargetId != 0 && isPlayerStuck)
        {
            var (monsterX, monsterY, monsterZ) = GetTargetMonsterCoordinates();

            // Calculate distance to monster
            int distanceX = Math.Abs(monsterX - currentX);
            int distanceY = Math.Abs(monsterY - currentY);
            int totalDistance = distanceX + distanceY;

            // If monster is too far away and we're not moving
            if (totalDistance > MAX_MONSTER_DISTANCE)
            {
                Console.WriteLine($"[DEBUG] Target monster (ID: {currentTargetId}) is too far away ({totalDistance} sqm) and player is stuck. Blacklisting for {TARGET_BLACKLIST_DURATION.TotalSeconds} seconds.");

                // Blacklist this target ID
                blacklistedTargetTimers[currentTargetId] = DateTime.Now.Add(TARGET_BLACKLIST_DURATION);

                // Cancel the current target
                InstantSendKeyPress(VK_F6);

                // Reset the stuck timer
                lastPositionTime = DateTime.MinValue;

                hasBlacklisted = true;
            }
        }

        return hasBlacklisted;
    }

    static void DisplayBlacklistedTargets()
    {
        if (blacklistedTargetTimers.Count == 0)
            return;

        var now = DateTime.Now;
        Console.WriteLine("\nBlacklisted Targets:");
        Console.WriteLine(new string('-', 40));

        foreach (var kvp in blacklistedTargetTimers)
        {
            TimeSpan remainingTime = kvp.Value - now;
            if (remainingTime.TotalSeconds > 0)
            {
                Console.WriteLine($"  Target ID: {kvp.Key}, Time remaining: {remainingTime.TotalSeconds:F1} seconds");
            }
        }
    }
}
