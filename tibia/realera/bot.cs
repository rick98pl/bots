using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Drawing;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using System.Windows.Forms;
using System.Drawing;



class Program
{
    [DllImport("kernel32.dll")]
    static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int processId);
    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
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
    [DllImport("user32.dll")]
    static extern bool SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    const int PROCESS_VM_READ = 0x0010;
    const int PROCESS_VM_WRITE = 0x0020;
    const int PROCESS_VM_OPERATION = 0x0008;
    const uint WM_KEYDOWN = 0x0100;
    const uint WM_KEYUP = 0x0101;
    const int VK_LEFT = 0x25;
    const int VK_UP = 0x26;
    const int VK_RIGHT = 0x27;
    const int VK_DOWN = 0x28;
    static extern bool SetForegroundWindow(IntPtr hWnd);
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
    const int DEFAULT_BEEP_HP_THRESHOLD = 1;
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
        follow = 0,
        currentOutfit = 0,
        invisibilityCode = 0;



    static bool memoryReadActive = false;
    static bool programRunning = true;
    static bool autoPotionActive = true;
    static bool isRecording = false;
    static bool isPlaying = false;
    static bool shouldRestartMemoryThread = false;

    // First, add these variables at the class level where your other state variables are defined
    private static int lastKnownMonsterX = 0;
    private static int lastKnownMonsterY = 0;
    private static int lastKnownMonsterZ = 0;
    private static int lastKnownMonsterId = 0;

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
        //Console.WriteLine($"Default HP Key: {DEFAULT_HP_KEY_NAME}");
        //Console.WriteLine($"Default Mana Key: {DEFAULT_MANA_KEY_NAME}");
        threadFlags["recording"] = false;
        threadFlags["playing"] = false;
        threadFlags["autopot"] = true;
        threadFlags["spawnwatch"] = true;
        threadFlags["lootrecognizer"] = true; // Add this line

        threadFlags.TryAdd("overlay", true);
        // Add overlay flag with default set to true but CHANGE TO FALSE to reduce CPU
        if (!threadFlags.ContainsKey("overlay"))
        {
            threadFlags.TryAdd("overlay", false); // Default to OFF to save CPU
        }

        if (!threadFlags.ContainsKey("clickoverlay"))
        {
            threadFlags.TryAdd("clickoverlay", false); // Default to OFF to save CPU
        }

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
                        Sleep(2000); // Increased sleep when process not found
                        continue;
                    }
                    else if (processes.Length == 1)
                    {
                        selectedProcess = processes[0];
                        Console.WriteLine($"One process found: {selectedProcess.ProcessName} (ID: {selectedProcess.Id})");
                        Console.WriteLine($"Window Title: {selectedProcess.MainWindowTitle}");
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
                            selectedProcess = processes[choice - 1];
                            Console.WriteLine($"Selected process: {selectedProcess.ProcessName} (ID: {selectedProcess.Id})");
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
            const int PROCESS_ALL_ACCESS = 0x001F0FFF;
            processHandle = OpenProcess(PROCESS_ALL_ACCESS, false, selectedProcess.Id);
            moduleBase = selectedProcess.MainModule.BaseAddress;

            GetClientRect(targetWindow, out RECT windowRect);
            int windowHeight = windowRect.Bottom - windowRect.Top;
            //Console.WriteLine($"[DEBUG] Detected window height: {windowHeight}px");
            smallWindow = windowHeight < 1200;
            //Console.WriteLine($"[DEBUG] Using {(smallWindow ? "small window (1080p)" : "large window (1440p)")} settings");

            StartWorkerThreads();
            DisplayStats();
            DateTime lastOverlayCheck = DateTime.MinValue;
            DateTime lastInputCheck = DateTime.MinValue; // Add tracking for input checking
            const int INPUT_CHECK_INTERVAL = 50; // Check for input every 50ms
            const int OVERLAY_CHECK_INTERVAL = 2000; // Check overlay every 2 seconds

            while (memoryReadActive && !shouldRestartMemoryThread)
            {
                DateTime now = DateTime.Now;

                // Only check for input periodically to reduce CPU
                if ((now - lastInputCheck).TotalMilliseconds >= INPUT_CHECK_INTERVAL)
                {
                    lastInputCheck = now;

                    // Handle user input
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true).Key;
                        HandleUserInput(key);
                    }
                }

                // Periodically check overlay status (every 2 seconds)
                if ((now - lastOverlayCheck).TotalMilliseconds >= OVERLAY_CHECK_INTERVAL)
                {
                    lastOverlayCheck = now;

                    // Check if overlay should be running
                    if (threadFlags["overlay"] && (overlayForm == null || overlayForm.IsDisposed))
                    {
                        StartOverlay();
                    }
                    else if (!threadFlags["overlay"] && overlayForm != null && !overlayForm.IsDisposed)
                    {
                        StopOverlay();
                    }
                }

                Sleep(50); // Less frequent checking of main loop
            }

            if (shouldRestartMemoryThread)
            {
                shouldRestartMemoryThread = false;
                StopWorkerThreads();
                StopOverlay(); // Stop overlay if restarting memory thread
                selectedProcess = null;
            }
        }
    }

    // Add these variables at the top with other static variables
    static bool maintainOutfitActive = true;
    static int desiredOutfit = 75; // Starting value of 127
    static Thread outfitThread = null;
    static readonly object outfitLock = new object();

    // This function maintains the desired outfit, setting it initially and keeping it if game changes it
    static void MaintainOutfitThread()
    {
        //Console.WriteLine($"[OUTFIT] Maintenance thread started. Maintaining outfit {desiredOutfit}");

        // Reduce frequency of checks - save CPU
        const int CHECK_INTERVAL_MS = 500; // Only check every 500ms instead of 100ms
        DateTime lastCheckTime = DateTime.MinValue;

        try
        {
            // Set the initial outfit when thread starts to exactly 127
            int currentOutfitValue;
            lock (memoryLock)
            {
                currentOutfitValue = currentOutfit;
            }

            if (currentOutfitValue != desiredOutfit)
            {
                //Console.WriteLine($"[OUTFIT] Initial setting from {currentOutfitValue} to {desiredOutfit}");

                // Find the outfit variable address
                IntPtr outfitAddress = IntPtr.Zero;
                foreach (var variable in variables)
                {
                    if (variable.Name.Contains("currentOutfit"))
                    {
                        // Convert base address to IntPtr explicitly
                        IntPtr baseAddress = IntPtr.Add(moduleBase, (int)variable.BaseAddress);
                        byte[] buffer = new byte[4]; // Size for pointer

                        if (!ReadProcessMemory(processHandle, baseAddress, buffer, buffer.Length, out _))
                        {
                            //Console.WriteLine("[DEBUG] Failed to read outfit base address");
                            return;
                        }

                        outfitAddress = (IntPtr)BitConverter.ToInt32(buffer, 0);
                        outfitAddress = IntPtr.Add(outfitAddress, variable.Offsets[0]);
                        break;
                    }
                }

                if (outfitAddress == IntPtr.Zero)
                {
                    //Console.WriteLine("[DEBUG] Could not find outfit address");
                    return;
                }

                // Prepare the exact value buffer for 127
                byte[] exactOutfitBuffer = BitConverter.GetBytes(desiredOutfit);

                // Write the value to memory
                int bytesWritten;
                if (!WriteProcessMemory(processHandle, outfitAddress, exactOutfitBuffer, exactOutfitBuffer.Length, out bytesWritten))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    //Console.WriteLine($"[DEBUG] Failed to write initial outfit value. Error code: {errorCode}");
                    return;
                }

                // Update the local variable
                currentOutfit = desiredOutfit;
                //Console.WriteLine($"[OUTFIT] Successfully set initial outfit to {desiredOutfit}");

                Sleep(250); // Delay to allow change to register
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OUTFIT] Error during initial outfit setting: {ex.Message}");
        }

        // Main maintenance loop
        while (memoryReadActive && maintainOutfitActive)
        {
            try
            {
                DateTime now = DateTime.Now;

                // Only check periodically rather than continuously
                if ((now - lastCheckTime).TotalMilliseconds < CHECK_INTERVAL_MS)
                {
                    Sleep(50); // Short sleep to prevent CPU hammering
                    continue;
                }

                lastCheckTime = now;

                int currentOutfitValue;
                lock (memoryLock)
                {
                    currentOutfitValue = currentOutfit;
                }

                // Check if outfit has changed from desired value
                lock (outfitLock)
                {
                    if (currentOutfitValue != desiredOutfit)
                    {
                        //Console.WriteLine($"[OUTFIT] Detected outfit change from {desiredOutfit} to {currentOutfitValue}. Resetting...");

                        // Find the outfit variable address
                        IntPtr outfitAddress = IntPtr.Zero;
                        foreach (var variable in variables)
                        {
                            if (variable.Name.Contains("currentOutfit"))
                            {
                                // Convert base address to IntPtr explicitly
                                IntPtr baseAddress = IntPtr.Add(moduleBase, (int)variable.BaseAddress);
                                byte[] buffer = new byte[4]; // Size for pointer

                                if (!ReadProcessMemory(processHandle, baseAddress, buffer, buffer.Length, out _))
                                {
                                    //Console.WriteLine("[DEBUG] Failed to read outfit base address");
                                    break;
                                }

                                outfitAddress = (IntPtr)BitConverter.ToInt32(buffer, 0);
                                outfitAddress = IntPtr.Add(outfitAddress, variable.Offsets[0]);
                                break;
                            }
                        }

                        if (outfitAddress == IntPtr.Zero)
                        {
                            //Console.WriteLine("[DEBUG] Could not find outfit address");
                            Sleep(1000);
                            continue;
                        }

                        // Prepare the exact value buffer
                        byte[] exactOutfitBuffer = BitConverter.GetBytes(desiredOutfit);

                        // Write the value to memory
                        int bytesWritten;
                        if (!WriteProcessMemory(processHandle, outfitAddress, exactOutfitBuffer, exactOutfitBuffer.Length, out bytesWritten))
                        {
                            int errorCode = Marshal.GetLastWin32Error();
                            //Console.WriteLine($"[DEBUG] Failed to write outfit value. Error code: {errorCode}");
                            Sleep(1000);
                            continue;
                        }

                        // Update the local variable
                        currentOutfit = desiredOutfit;
                        //Console.WriteLine($"[OUTFIT] Successfully reset outfit to {desiredOutfit}");

                        Sleep(50); // Small delay after changing outfit
                    }
                }

                // Longer sleep between checks (was 100ms)
                Sleep(200);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OUTFIT] Error in outfit maintenance: {ex.Message}");
                Sleep(1000); // Longer delay after an error
            }
        }

        //Console.WriteLine("[OUTFIT] Maintenance thread stopped");
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

        // Add outfit maintenance thread
        Thread outfitThread = new Thread(MaintainOutfitThread);
        outfitThread.IsBackground = true;
        outfitThread.Name = "OutfitMaintenance";
        outfitThread.Start();

        // Add loot recognizer thread
        Thread lootRecognizerThread = new Thread(LootRecognizerThread);
        lootRecognizerThread.IsBackground = true;
        lootRecognizerThread.Name = "LootRecognizer";
        lootRecognizerThread.Start();

        StartClickOverlay();

        // Start SPAWNWATCHER if enabled
        if (threadFlags["spawnwatch"])
        {
            SPAWNWATCHER.Start(targetWindow, pixelSize);
        }

        //Console.WriteLine("Worker threads started successfully");
    }


    static void StopWorkerThreads()
    {
        memoryReadActive = false;
        threadFlags["recording"] = false;
        threadFlags["playing"] = false;
        threadFlags["autopot"] = false;
        threadFlags["overlay"] = false; // Also disable overlay
        lootRecognizerActive = false;   // Stop loot recognizer
        SPAWNWATCHER.Stop();
        StopPositionAlertSound(); // Stop any playing alert sounds

        // Explicitly stop the overlay
        StopOverlay();
        StopClickOverlay();

        //Console.WriteLine("Worker threads stopping...");
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
        },
        new Variable
        {
            Name = "currentOutfit",
            BaseAddress = (IntPtr)0x009432D0,
            Offsets = new List<int> { 88 },
            Type = "Int32"
        },
        new Variable
        {
            Name = "invisibilityCode",
            BaseAddress = (IntPtr)0x009432D0,
            Offsets = new List<int> { 84 },
            Type = "Int32"
        }
    };
    static void MemoryReadingThread()
    {
        DateTime lastDebugOutputTime = DateTime.MinValue;
        DateTime lastFullScanTime = DateTime.MinValue;
        const double DEBUG_COOLDOWN_SECONDS = 1.5;
        const double FULL_SCAN_INTERVAL_MS = 100; // Only do full scan every 100ms
        //Console.WriteLine("Memory reading thread started");

        int consecutiveFailures = 0;
        const int MAX_FAILURES = 5;

        while (memoryReadActive)
        {
            try
            {
                // Check for process exit less frequently
                if (consecutiveFailures == 0 && selectedProcess.HasExited)
                {
                    shouldRestartMemoryThread = true;
                    break;
                }

                DateTime now = DateTime.Now;
                bool shouldDoFullScan = (now - lastFullScanTime).TotalMilliseconds >= FULL_SCAN_INTERVAL_MS;

                if (shouldDoFullScan)
                {
                    lastFullScanTime = now;

                    foreach (var variable in variables)
                    {
                        try
                        {
                            // Convert base address to IntPtr explicitly
                            IntPtr address = IntPtr.Add(moduleBase, (int)variable.BaseAddress);
                            byte[] buffer;

                            if (variable.Offsets.Count > 0)
                            {
                                buffer = new byte[4]; // Size for pointer
                                if (!ReadProcessMemory(processHandle, address, buffer, buffer.Length, out _))
                                    continue;

                                address = (IntPtr)BitConverter.ToInt32(buffer, 0);
                                address = IntPtr.Add(address, variable.Offsets[0]);
                            }

                            // Set buffer size based on type
                            buffer = new byte[8];

                            if (!ReadProcessMemory(processHandle, address, buffer, buffer.Length, out _))
                                continue;

                            // Special debug for currentOutfit
                            if (variable.Name.Contains("currentOutfit"))
                            {
                                int rawValue = BitConverter.ToInt32(buffer, 0);
                                currentOutfit = rawValue;
                            }
                            else if (variable.Name.Contains("invisibilityCode"))
                            {
                                int rawValue = BitConverter.ToInt32(buffer, 0);
                                invisibilityCode = rawValue;
                            }
                            else if (variable.Type == "Double")
                            {
                                double value = BitConverter.ToDouble(buffer, 0);
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
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[DEBUG] Error reading variable {variable.Name}: {ex.Message}");
                        }
                    }

                    // Always read position data which is critical for gameplay
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
                            DateTime debugNow = DateTime.Now;
                            if ((debugNow - lastDebugOutputTime).TotalSeconds >= DEBUG_COOLDOWN_SECONDS)
                            {
                                //Console.WriteLine($"[DEBUG] In chase: targetId={targetId}, position=({posX},{posY},{posZ})");
                                lastDebugOutputTime = debugNow;
                            }
                        }
                    }

                    // Check position distance if we have loaded coordinates
                    if (loadedCoords != null && loadedCoords.cords.Count > 0 && threadFlags["playing"])
                    {
                        CheckPositionDistance(loadedCoords.cords);
                    }

                    // Reset failure counter on successful scans
                    consecutiveFailures = 0;
                }
                else
                {
                    // On off-cycles, only read critical data like player position and target
                    lock (memoryLock)
                    {
                        posX = ReadInt32(processHandle, moduleBase, xAddressOffset);
                        posY = ReadInt32(processHandle, moduleBase, yAddressOffset);
                        targetId = ReadInt32(processHandle, moduleBase, targetIdOffset);
                    }
                }

                // Adjust sleep time to reduce CPU usage while still maintaining responsiveness
                // Sleep longer during non-combat, shorter during combat for responsiveness
                int sleepTime = (targetId == 0) ? 50 : 20;
                Sleep(sleepTime);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Memory reading error: {ex.Message}");
                consecutiveFailures++;

                if (consecutiveFailures >= MAX_FAILURES)
                {
                    //Console.WriteLine("Too many consecutive errors in memory reading thread. Restarting...");
                    shouldRestartMemoryThread = true;
                    break;
                }

                // Sleep longer after errors to avoid CPU hammering
                Sleep(500);
            }
        }
        //Console.WriteLine("Memory reading thread exited");
    }


    static void ChangeOutfit(int change)
    {
        try
        {
            // Get the current outfit before modification
            int currentOutfitVar;
            lock (memoryLock)
            {
                currentOutfitVar = currentOutfit;
            }
            int previousOutfit = currentOutfitVar;

            // Calculate the new outfit value (add the change which could be +1 or -1)
            int newOutfit = previousOutfit + change;

            // Ensure the new outfit value is within valid range (assuming 0-99999 range for outfits)
            const int MIN_OUTFIT = 0;
            const int MAX_OUTFIT = 99999;

            //Console.WriteLine($"[DEBUG] CurrenntOutfit: {previousOutfit}");
            //Console.WriteLine($"[DEBUG] Changing to: {newOutfit}");

            if (newOutfit < MIN_OUTFIT)
                newOutfit = MIN_OUTFIT;
            if (newOutfit > MAX_OUTFIT)
                newOutfit = MAX_OUTFIT;

            // Skip operation if no change
            if (newOutfit == previousOutfit)
            {
                //Console.WriteLine($"[DEBUG] Outfit already at {(change > 0 ? "maximum" : "minimum")} value: {previousOutfit}");
                return;
            }

            // Write to memory to change the outfit
            IntPtr outfitAddress = IntPtr.Zero;

            // Find the outfit variable from the variables list
            foreach (var variable in variables)
            {
                if (variable.Name.Contains("currentOutfit"))
                {
                    // Convert base address to IntPtr explicitly
                    IntPtr baseAddress = IntPtr.Add(moduleBase, (int)variable.BaseAddress);
                    byte[] buffer = new byte[4]; // Size for pointer

                    if (!ReadProcessMemory(processHandle, baseAddress, buffer, buffer.Length, out _))
                    {
                        //Console.WriteLine("[DEBUG] Failed to read outfit base address");
                        return;
                    }

                    outfitAddress = (IntPtr)BitConverter.ToInt32(buffer, 0);
                    outfitAddress = IntPtr.Add(outfitAddress, variable.Offsets[0]);
                    break;
                }
            }

            if (outfitAddress == IntPtr.Zero)
            {
                //Console.WriteLine("[DEBUG] Could not find outfit address");
                return;
            }

            // Prepare the new value buffer
            byte[] newOutfitBuffer = BitConverter.GetBytes(newOutfit);

            // Write the new value to memory
            int bytesWritten;
            if (!WriteProcessMemory(processHandle, outfitAddress, newOutfitBuffer, newOutfitBuffer.Length, out bytesWritten))
            {
                int errorCode = Marshal.GetLastWin32Error();
                //Console.WriteLine($"[DEBUG] Failed to write new outfit value. Error code: {errorCode}");
                return;
            }

            // Update the local variable to maintain consistency
            currentOutfit = newOutfit;

            // **** NEW ADDITION: Update the desired outfit for maintenance when manually changed ****
            lock (outfitLock)
            {
                desiredOutfit = newOutfit;
                //Console.WriteLine($"[OUTFIT] Updated desired outfit to: {desiredOutfit}");
            }

            //Console.WriteLine($"[DEBUG] Changed outfit from {previousOutfit} to {newOutfit}");


        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error changing outfit: {ex.Message}");
        }
    }

    // Add method to explicitly set the desired outfit value



    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool WriteProcessMemory(
    IntPtr hProcess,
    IntPtr lpBaseAddress,
    byte[] lpBuffer,
    int nSize,
    out int lpNumberOfBytesWritten
);

    static void AutoPotionThread()
    {
        //Console.WriteLine("Auto-potion thread started");
        DateTime lastStatsDisplay = DateTime.MinValue;
        const int STATS_DISPLAY_INTERVAL_MS = 2000; // Update stats every 2 seconds

        while (memoryReadActive)
        {
            try
            {
                DateTime now = DateTime.Now;

                // Only update the display every few seconds rather than on every cycle
                if ((now - lastStatsDisplay).TotalMilliseconds >= STATS_DISPLAY_INTERVAL_MS)
                {
                    lastStatsDisplay = now;
                }

                if (threadFlags["autopot"] && targetWindow != IntPtr.Zero)
                {
                    var thresholdms = 1000;
                    double hpPercent, manaPercent;

                    lock (memoryLock)
                    {
                        hpPercent = (curHP / maxHP) * 100;
                        manaPercent = (curMana / maxMana) * 100;
                    }

                    if (hpPercent == 0)
                    {
                        Sleep(50); // Increased sleep time when no data
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
                            //Console.WriteLine(
                            ////$"⚠ HP below threshold ({DEFAULT_HP_THRESHOLD}%), current HP: {curHP}/{maxHP} ({hpPercent:F1}%), sending {DEFAULT_HP_KEY_NAME}"
                            ////);
                            SendKeyPress(DEFAULT_HP_KEY);
                            lastHpActionTime = now.AddMilliseconds(random.Next(0, 100));
                        }
                    }

                    if (manaPercent <= DEFAULT_MANA_THRESHOLD)
                    {
                        if ((now - lastManaActionTime).TotalMilliseconds >= thresholdms)
                        {
                            //Console.WriteLine(
                            ////$"⚠ Mana below threshold ({DEFAULT_MANA_THRESHOLD}%), sending {DEFAULT_MANA_KEY_NAME}"
                            ////);
                            SendKeyPress(DEFAULT_MANA_KEY);
                            lastManaActionTime = now.AddMilliseconds(random.Next(0, 100));
                        }
                    }
                }

                // Increased sleep time to reduce CPU usage
                Sleep(250);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Auto-potion error: {ex.Message}");
                Sleep(1000);
            }
        }
        //Console.WriteLine("Auto-potion thread exited");
    }
    static void DisplayStats()
    {

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
        //Console.WriteLine("RealeraDX - Live Stats:\n");
        //Console.WriteLine("{0,-20} {1,15}", "Metric", "Value");
        //Console.WriteLine(new string('-', 40));
        //Console.WriteLine("{0,-20} {1,15:F0}", "Current HP", curHP);
        //Console.WriteLine("{0,-20} {1,15:F0}", "Max HP", maxHP);
        //Console.WriteLine("{0,-20} {1,15:F1}%", "HP %", hpPercent);
        //Console.WriteLine("{0,-20} {1,15:F0}", "Current Mana", curMana);
        //Console.WriteLine("{0,-20} {1,15:F0}", "Max Mana", maxMana);
        //Console.WriteLine("{0,-20} {1,15:F1}%", "Mana %", manaPercent);
        //Console.WriteLine("{0,-20} {1,15:F0}", "targetId", targetId);
        //Console.WriteLine("{0,-20} {1,15:F0}", "follow", follow);
        //Console.WriteLine(new string('-', 40));
        //Console.WriteLine($"Position: X={currentX}, Y={currentY}, Z={currentZ}");
        //Console.WriteLine($"Outfit: {currentOutfit}\n");
        //Console.WriteLine($"InvisibilityCode: {invisibilityCode}\n");
        //Console.WriteLine("\n");
        if (threadFlags["recording"])
        {
            //Console.WriteLine("🔴 Recording coordinates...");
            //Console.WriteLine($"Coordinates recorded: {recordedCoords.Count}");
        }
        if (threadFlags["playing"] && currentTarget != null)
        {
            //Console.WriteLine("\n▶️ PLAYING PATH:");
            //Console.WriteLine(new string('-', 40));
            //Console.WriteLine(
            ////$"Progress: {currentCoordIndex + 1}/{totalCoords} ({(((float)(currentCoordIndex + 1) / totalCoords) * 100):F1}%)"
            ////);
            //Console.WriteLine($"Current: X={currentX}, Y={currentY}, Z={currentZ}");
            //Console.WriteLine(
            ////$"Target:  X={currentTarget.X}, Y={currentTarget.Y}, Z={currentTarget.Z}"
            ////);
            int distanceX = Math.Abs(currentTarget.X - currentX);
            int distanceY = Math.Abs(currentTarget.Y - currentY);
            //Console.WriteLine($"Distance: {distanceX + distanceY} steps");
            int barLength = 20;
            int progress = (int)Math.Round(
                (double)(currentCoordIndex + 1) / totalCoords * barLength
            );
            //Console.Write("[");
            for (int i = 0; i < barLength; i++)
            {
                //Console.Write(i < progress ? "█" : " ");
            }
            //Console.WriteLine($"] {(((float)(currentCoordIndex + 1) / totalCoords) * 100):F1}%");
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
                Console.WriteLine($"Auto-potions {(threadFlags["autopot"] ? "enabled" : "disabled")}");
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
            case ConsoleKey.W:
                // Toggle spawn watcher
                bool isActive = SPAWNWATCHER.Toggle(targetWindow, pixelSize);
                threadFlags["spawnwatch"] = isActive;
                Console.WriteLine($"Spawn watcher {(isActive ? "enabled" : "disabled")}");
                break;
            case ConsoleKey.F:
                // Decrease outfit by 1
                ChangeOutfit(-1);
                break;
            case ConsoleKey.G:
                // Increase outfit by 1
                ChangeOutfit(1);
                break;
            case ConsoleKey.H:
                // Decrease outfit by 1
                ChangeOutfit(-10);
                break;
            case ConsoleKey.J:
                // Increase outfit by 1
                ChangeOutfit(10);
                break;
            case ConsoleKey.O: // Add a key to toggle overlay
                threadFlags["overlay"] = !threadFlags["overlay"];
                //Console.WriteLine($"Overlay {(threadFlags["overlay"] ? "enabled" : "disabled")}");
                if (threadFlags["overlay"])
                {
                    StartOverlay();
                }
                else
                {
                    StopOverlay();
                }
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
    private static readonly TimeSpan F6Cooldown = TimeSpan.FromSeconds(0.7);

    static void SendKeyPress(int key)
    {
        if (key == 0x75) // F6 key
        {
            DateTime currentTime = DateTime.Now;

            if (!canPressF6 || (currentTime - lastF6Press) < F6Cooldown)
            {
                //Console.WriteLine("F6 key blocked - cooldown active");
                return;
            }

            //Console.WriteLine("F6 key pressed - starting cooldown");
            lastF6Press = currentTime;
            canPressF6 = false;

            System.Threading.Timer cooldownTimer = null;
            cooldownTimer = new System.Threading.Timer((state) =>
            {
                canPressF6 = true;
                //Console.WriteLine("F6 key cooldown finished");
                cooldownTimer?.Dispose();
            }, null, F6Cooldown, TimeSpan.Zero);
        }

        SendMessage(targetWindow, WM_KEYDOWN, (IntPtr)key, IntPtr.Zero);
        Sleep(random.Next(10, 25));
        SendMessage(targetWindow, WM_KEYUP, (IntPtr)key, IntPtr.Zero);
    }

    static void InstantSendKeyPress(int key)
    {
        SendMessage(targetWindow, WM_KEYDOWN, (IntPtr)key, IntPtr.Zero);
        Sleep(random.Next(10, 25));
        SendMessage(targetWindow, WM_KEYUP, (IntPtr)key, IntPtr.Zero);
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
                //Console.WriteLine("[DEBUG] Moving EAST with arrow key");
                SendKeyPress(VK_RIGHT);
            }
            else if (diffX < 0)
            {
                //Console.WriteLine("[DEBUG] Moving WEST with arrow key");
                SendKeyPress(VK_LEFT);
            }
        }
        else
        {
            // Move in Y direction first if it's farther
            if (diffY > 0)
            {
                //Console.WriteLine("[DEBUG] Moving SOUTH with arrow key");
                SendKeyPress(VK_DOWN);
            }
            else if (diffY < 0)
            {
                //Console.WriteLine("[DEBUG] Moving NORTH with arrow key");
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

    static bool firstFound = true;


    // Add these variables at the class level with your other state variables
    private static DateTime lastWaypointClickTime = DateTime.MinValue;
    private static bool waypointStuckDetectionEnabled = true;
    private static TimeSpan waypointStuckTimeout = TimeSpan.FromSeconds(2); // Time to wait before considering player stuck
    private static int consecutiveStuckCount = 0;
    private static int maxConsecutiveStuckCount = 3; // After this many stuck attempts, disable randomization temporarily
    private static Coordinate lastClickedWaypoint = null;
    private static List<Point> failedClickPoints = new List<Point>(); // Store points where clicks failed
    private static int waypointRetryCount = 0;
    private static readonly int MAX_WAYPOINT_RETRIES = 3;
    private static DateTime lastFailedClickPointsCleanupTime = DateTime.MinValue;
    private static readonly TimeSpan FAILED_CLICK_POINTS_CLEANUP_INTERVAL = TimeSpan.FromMinutes(5); // Clean failed points list every 5 minutes

    // Update the ClickWaypoint method to include timestamp tracking
    static bool ClickWaypoint(Coordinate target)
    {
        try
        {
            bool isChaseReturnPoint = chaseTracker.ShouldReturnToStart() &&
                                 target.X == chaseTracker.GetReturnPosition().X &&
                                 target.Y == chaseTracker.GetReturnPosition().Y;

            if (isChaseReturnPoint)
            {
                //Console.WriteLine("[DEBUG] Clicking waypoint that is a chase return position");
            }

            int currentX, currentY;
            lock (memoryLock)
            {
                currentX = posX;
                currentY = posY;
            }
            GetClientRect(targetWindow, out RECT rect);
            int baseX = (rect.Right - rect.Left) / 2 - 186;
            int baseY = (rect.Bottom - rect.Top) / 2 - baseYOffset;

            int diffX = target.X - currentX;
            int diffY = target.Y - currentY;
            int targetX = baseX + (diffX * pixelSize);
            int targetY = baseY + (diffY * pixelSize);

            if (GetTargetId() != 0)
            {
                //Console.WriteLine("[DEBUG] Combat detected, canceling movement");
                return false;
            }

            // Check if this click point has previously failed
            Point clickPoint = new Point(targetX, targetY);
            if (failedClickPoints.Any(p => Math.Abs(p.X - clickPoint.X) < 5 && Math.Abs(p.Y - clickPoint.Y) < 5))
            {
                //Console.WriteLine("[DEBUG] This click point previously failed, trying a different location");
                return false;
            }

            int lParam = (targetY << 16) | (targetX & 0xFFFF);
            SendKeyPress(VK_ESCAPE);
            Sleep(25);
            SendMessage(targetWindow, 0x0200, IntPtr.Zero, (IntPtr)lParam);
            Sleep(1);
            SendMessage(targetWindow, WM_LBUTTONDOWN, (IntPtr)1, (IntPtr)lParam);
            Sleep(1);
            SendMessage(targetWindow, WM_LBUTTONUP, IntPtr.Zero, (IntPtr)lParam);
            Sleep(1);
            int centerLParam = (baseY << 16) | (baseX & 0xFFFF);

            // Record the waypoint click with special color
            RecordWaypointClick(targetX, targetY);

            // Store the clicked waypoint and timestamp
            lastClickedWaypoint = new Coordinate { X = target.X, Y = target.Y, Z = target.Z };
            lastWaypointClickTime = DateTime.Now;

            //Console.WriteLine(
            ////$"[DEBUG] Clicked: X={diffX}, Y={diffY} (Screen: {targetX}, {targetY}, Pixel Size: {pixelSize})"
            ////);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Click Error: {ex.Message}");
            return false;
        }
    }

    // Modify the PlayCoordinates method to check for stuck player
    static void PlayCoordinates()
    {
        UpdateUIPositions();

        Console.WriteLine("Path playback starting...");
        string json = File.ReadAllText(cordsFilePath);
        loadedCoords = JsonSerializer.Deserialize<CoordinateData>(json);
        if (loadedCoords == null || loadedCoords.cords.Count == 0)
        {
            //Console.WriteLine("No coordinates found in cords.json!");
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

        // Reset stuck detection variables
        consecutiveStuckCount = 0;
        failedClickPoints.Clear();
        waypointRetryCount = 0;

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

                // Check for stuck player if we have a recent waypoint click
                if (waypointStuckDetectionEnabled &&
                    lastWaypointClickTime != DateTime.MinValue &&
                    lastClickedWaypoint != null)
                {
                    // Check how long it's been since the click
                    TimeSpan timeSinceClick = DateTime.Now - lastWaypointClickTime;

                    // If enough time has passed to check for stuck state
                    if (timeSinceClick > waypointStuckTimeout)
                    {
                        // Calculate distance moved since the click
                        int distanceMovedX = Math.Abs(lastClickedWaypoint.X - currentX);
                        int distanceMovedY = Math.Abs(lastClickedWaypoint.Y - currentY);
                        int totalDistanceMoved = distanceMovedX + distanceMovedY;

                        // If player hasn't moved significantly since clicking
                        if (totalDistanceMoved <= 1) // Threshold for "hasn't moved"
                        {
                            //Console.WriteLine($"[STUCK DETECTION] Player appears to be stuck! Hasn't moved in {timeSinceClick.TotalSeconds:F1} seconds");

                            // Add the failed click point to our list
                            GetClientRect(targetWindow, out RECT rect);
                            int baseX = (rect.Right - rect.Left) / 2 - 186;
                            int baseY = (rect.Bottom - rect.Top) / 2 - baseYOffset;
                            int diffX = lastClickedWaypoint.X - currentX;
                            int diffY = lastClickedWaypoint.Y - currentY;
                            int clickX = baseX + (diffX * pixelSize);
                            int clickY = baseY + (diffY * pixelSize);
                            failedClickPoints.Add(new Point(clickX, clickY));
                            //Console.WriteLine($"[STUCK DETECTION] Added failed click point at screen coordinates ({clickX}, {clickY})");
                            //Console.WriteLine($"[STUCK DETECTION] Total failed click points: {failedClickPoints.Count}");

                            // Increment stuck counter
                            consecutiveStuckCount++;
                            waypointRetryCount++;

                            // If we've been stuck too many times, temporarily disable randomization
                            if (consecutiveStuckCount >= maxConsecutiveStuckCount)
                            {
                                //Console.WriteLine("[STUCK DETECTION] Too many consecutive stuck instances. Temporarily disabling waypoint randomization.");
                                waypointRandomizationEnabled = false;
                            }

                            // Reset the click time to prevent repeated detections
                            lastWaypointClickTime = DateTime.MinValue;
                            lastClickedWaypoint = null;

                            // Try to handle the stuck situation based on retry count
                            if (waypointRetryCount >= MAX_WAYPOINT_RETRIES)
                            {
                                //Console.WriteLine($"[STUCK DETECTION] Max retries reached ({MAX_WAYPOINT_RETRIES}). Switching to arrow key movement.");

                                // First try pressing escape to clear any potential UI elements
                                //Console.WriteLine("[STUCK DETECTION] Pressing Escape to clear any UI elements");
                                SendKeyPress(VK_ESCAPE);
                                Sleep(100);

                                // Get next waypoint and use arrow key movement instead
                                Coordinate nextWaypointy = FindNextWaypoint(
                                    ref waypoints,
                                    currentX,
                                    currentY,
                                    currentZ,
                                    ref currentCoordIndex
                                );

                                // Move with arrow keys
                                //Console.WriteLine($"[STUCK DETECTION] Trying arrow key movement to ({nextWaypointy.X}, {nextWaypointy.Y})");
                                MoveCharacterTowardsWaypoint(currentX, currentY, nextWaypointy.X, nextWaypointy.Y);
                                SendKeyPress(VK_F6);
                                Sleep(300);

                                // Try a second arrow movement in a different direction
                                lock (memoryLock)
                                {
                                    currentX = posX;
                                    currentY = posY;
                                }

                                //Console.WriteLine($"[STUCK DETECTION] Trying a second arrow movement in a different direction");
                                // Pick a different direction - try perpendicular movement
                                int theX = nextWaypointy.X - currentX;
                                int theY = nextWaypointy.Y - currentY;

                                // If moving more in X, try moving in Y instead, and vice versa
                                if (Math.Abs(theX) >= Math.Abs(theY))
                                {
                                    // Move in Y direction
                                    if (theY > 0)
                                        SendKeyPress(VK_DOWN);
                                    else
                                        SendKeyPress(VK_UP);
                                }
                                else
                                {
                                    // Move in X direction
                                    if (theX > 0)
                                        SendKeyPress(VK_RIGHT);
                                    else
                                        SendKeyPress(VK_LEFT);
                                }

                                SendKeyPress(VK_F6);
                                Sleep(300);

                                waypointRetryCount = 0; // Reset retry count after arrow key movement
                            }
                            else
                            {
                                // Skip to find a new waypoint
                                currentCoordIndex = FindClosestWaypointIndex(waypoints, currentX, currentY, currentZ);
                                //Console.WriteLine($"[STUCK DETECTION] Resetting to closest waypoint: index {currentCoordIndex}");
                            }

                            // Continue to next iteration to find a new waypoint
                            continue;
                        }
                        else
                        {
                            // Player has moved, reset stuck counter
                            if (consecutiveStuckCount > 0)
                            {
                                //Console.WriteLine($"[STUCK DETECTION] Player is moving normally again. Resetting stuck counter.");
                                consecutiveStuckCount = 0;
                                waypointRetryCount = 0;

                                // Re-enable randomization if it was disabled
                                if (!waypointRandomizationEnabled)
                                {
                                    //Console.WriteLine("[STUCK DETECTION] Re-enabling waypoint randomization.");
                                    waypointRandomizationEnabled = true;
                                }
                            }

                            // Reset click time since player is moving
                            lastWaypointClickTime = DateTime.MinValue;
                            lastClickedWaypoint = null;
                        }
                    }
                }

                // Periodically clean up the failed click points list to prevent it from growing too large
                DateTime now = DateTime.Now;
                if (failedClickPoints.Count > 0 &&
                    (now - lastFailedClickPointsCleanupTime) > FAILED_CLICK_POINTS_CLEANUP_INTERVAL)
                {
                    int oldCount = failedClickPoints.Count;
                    // Keep only the 20 most recent failed points
                    if (failedClickPoints.Count > 20)
                    {
                        failedClickPoints = failedClickPoints.Skip(failedClickPoints.Count - 20).ToList();
                    }

                    //Console.WriteLine($"[STUCK DETECTION] Cleaned up failed click points list: {oldCount} -> {failedClickPoints.Count}");
                    lastFailedClickPointsCleanupTime = now;
                }

                // The rest of your PlayCoordinates method continues here...
                // (Existing combat detection, waypoint finding, etc.)

                if (previousTargetId != 0 && currentTargetId == 0)
                {
                    lock (memoryLock)
                    {
                        // Force update the chase tracker with latest position after killing the monster
                        chaseTracker.Update(posX, posY, posZ, targetId);
                    }

                    if (chaseTracker.ShouldReturnToStart())
                    {
                        //Console.WriteLine("[DEBUG] Need to return to chase start position");
                    }
                    else
                    {
                        //Console.WriteLine("[DEBUG] No need to return to chase start position");
                    }


                    ToggleRing(targetWindow, false);
                    Sleep(1);

                    if (!clickedAroundTargets.Contains(previousTargetId) && previousTargetId != 0)
                    {
                        //Console.WriteLine($"[DEBUG] Fight finished, calling ClickAroundCharacter for target ID: {previousTargetId}");
                        clickedAroundTargets.Add(previousTargetId); // Track that we've clicked for this target

                        // Perform click around and wait for it to complete
                        bool targetFoundDuringClickAround = ClickAroundCharacter(targetWindow);
                    }


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

                if (currentTargetId != 0 && currentTargetId != lastTrackedTargetId)
                {
                    // A new target appeared, clear the tracking
                    //Console.WriteLine($"[DEBUG] New target ID detected: {currentTargetId}, resetting click tracking");
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
                        //Console.WriteLine($"[DEBUG] Chase began at position: X={currentX}, Y={currentY}, Z={currentZ}");
                    }

                    if (blacklistedTargets.Contains(currentTargetId))
                    {

                        //Console.WriteLine(
                        ////$"[DEBUG] Skipping blacklisted target ID: {currentTargetId}"
                        ////);
                        Sleep(1);
                        continue;
                    }
                    if (
                        !string.IsNullOrEmpty(monsterName)
                        && blacklistedMonsterNames.Contains(monsterName)
                    )
                    {

                        //Console.WriteLine(
                        ////$"[DEBUG] Skipping blacklisted monster: {monsterName}"
                        ////);
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
                    // Check if click around is in progress - never press F6 while click around is happening
                    if (isClickAroundInProgress)
                    {
                        //Console.WriteLine("[DEBUG] Click around operation in progress, skipping F6");
                    }
                    // Check if F6 was pressed very recently
                    else if ((DateTime.Now - lastF6Press).TotalMilliseconds < F6Cooldown.TotalMilliseconds)
                    {
                        TimeSpan remainingCooldown = F6Cooldown - (DateTime.Now - lastF6Press);
                        //Console.WriteLine($"[DEBUG] F6 cooldown active: {remainingCooldown.TotalMilliseconds:F0}ms remaining");
                    }
                }

                lock (memoryLock)
                {
                    currentTargetId = targetId;
                }
                if (currentTargetId != 0)
                {

                    //Console.WriteLine($"[DEBUG] Found target: {currentTargetId}");
                    continue;
                }
                //Console.WriteLine("[DEBUG] No target found, proceeding with movement");
                Sleep(1);
                Coordinate nextWaypoint = FindNextWaypoint(
                    ref waypoints,
                    currentX,
                    currentY,
                    currentZ,
                    ref currentCoordIndex
                );
                currentTarget = nextWaypoint;
                //Console.WriteLine(
                ////$"[DEBUG] Moving to waypoint: X={nextWaypoint.X}, Y={nextWaypoint.Y}, Z={nextWaypoint.Z}"
                ////);
                int distanceX = Math.Abs(nextWaypoint.X - currentX);
                int distanceY = Math.Abs(nextWaypoint.Y - currentY);
                int totalDistance = distanceX + distanceY;
                if (distanceX > 5 || distanceY > 5)
                {

                    //Console.WriteLine(
                    ////$"[DEBUG] Waypoint too far to click: X={nextWaypoint.X}, Y={nextWaypoint.Y}, Z={nextWaypoint.Z}"
                    ////);

                    //Console.WriteLine(
                    ////$"[DEBUG] Current position: X={currentX}, Y={currentY}, Z={currentZ}"
                    ////);

                    //Console.WriteLine($"[DEBUG] Distance: X={distanceX}, Y={distanceY}");
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
                        SendKeyPress(VK_F6);
                        Sleep(250);
                        lock (memoryLock)
                        {
                            currentX = posX;
                            currentY = posY;
                            currentZ = posZ;
                        }
                        distanceX = Math.Abs(nextWaypoint.X - currentX);
                        distanceY = Math.Abs(nextWaypoint.Y - currentY);

                        //Console.WriteLine(
                        ////$"[DEBUG] After move #{movesMade + 1}, new position: X={currentX}, Y={currentY}"
                        ////);

                        //Console.WriteLine(
                        ////$"[DEBUG] New distance: X={distanceX}, Y={distanceY}"
                        ////);
                        movesMade++;
                        if (distanceX <= 5 && distanceY <= 5)
                        {

                            //Console.WriteLine("[DEBUG] Now within clickable range");
                            break;
                        }
                        if (movesMade % 2 == 0)
                        {

                            //Console.WriteLine("[DEBUG] Switching movement priority");
                        }
                    }
                    if (distanceX > 5 || distanceY > 5)
                    {

                        //Console.WriteLine(
                        ////"[DEBUG] Still too far after arrow movement, finding a new waypoint"
                        ////);
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
                        //Console.WriteLine($"[DEBUG] While returning from chase, found close waypoint (dx={distanceToWaypointX}, dy={distanceToWaypointY}), stopping return");
                        chaseTracker.CompleteReturn();
                        isReturningFromChase = false;

                        // Skip the movement code below and continue with the next loop iteration
                        // to allow normal waypoint selection on the next cycle
                        continue;
                    }

                    //Console.WriteLine("[DEBUG] Using arrow keys to return from chase");
                    // Move step by step with arrow keys instead of clicking
                    MoveCharacterTowardsWaypoint(currentX, currentY, nextWaypoint.X, nextWaypoint.Y);
                    SendKeyPress(VK_F6);
                    Sleep(100); // Give the game time to process the movement
                    continue;   // Skip the normal waypoint clicking
                }
                //Console.WriteLine(
                ////$"[DEBUG] Clicking waypoint: X={nextWaypoint.X}, Y={nextWaypoint.Y}, Z={nextWaypoint.Z}"
                ////);
                bool clickSuccess = ClickWaypoint(nextWaypoint);
                //Console.WriteLine(
                ////$"[DEBUG] Clicking waypoint: X={nextWaypoint.X}, Y={nextWaypoint.Y}, Z={nextWaypoint.Z}"
                ////);

                // Remove the Sleep(2500) and replace with this destination-checking code
                if (clickSuccess)
                {
                    const int wp_DISTANCE_THRESHOLD = 1; // 2 sqm threshold
                    const int wp_MAX_WAIT_TIME_MS = 1000; // 10 seconds max wait time

                    DateTime wp_startTime = DateTime.Now;
                    bool wp_reachedDestination = false;

                    //Console.WriteLine("[DEBUG] Waiting for character to reach destination...");

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

                        if (currentTargetId == 0)
                        {
                            firstFound = false;
                            Thread.Sleep(300);
                        }
                        if (!firstFound && currentTargetId == 0)
                        {
                            firstFound = true;
                            SendKeyPress(VK_F6);
                            // Keep the crucial Sleep(1) as you mentioned
                            Sleep(10); //CRUCIAL YOU CANT SPAM F6 because targetId is getting weird!!! even if no monster
                            lock (memoryLock)
                            {
                                currentTargetId = targetId;
                            }
                            if (currentTargetId != 0)
                            {
                                ToggleRing(targetWindow, true);
                                //Console.WriteLine(
                                ////"[DEBUG] Target found during movement, switching to combat"
                                ////);
                                break;
                            }
                            else
                            {
                                ToggleRing(targetWindow, false);
                            }
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
                        firstFound = false;
                        Thread.Sleep(300);
                    }

                    if (!firstFound && currentTargetId == 0)
                    {
                        firstFound = true;
                        SendKeyPress(VK_F6);
                        // Keep the crucial Sleep(1) as you mentioned
                        Sleep(1); //CRUCIAL YOU CANT SPAM F6 because targetId is getting weird!!! even if no monster
                        lock (memoryLock)
                        {
                            currentTargetId = targetId;
                        }
                        if (currentTargetId != 0)
                        {
                            //Console.WriteLine(
                            ////"[DEBUG] Target found after movement, switching to combat"
                            ////);
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
        //Console.WriteLine("Path playback ended");
    }

    // Updated FindNextWaypoint method with improved randomization handling
    static Coordinate FindNextWaypoint(
        ref List<Coordinate> waypoints,
        int currentX,
        int currentY,
        int currentZ,
        ref int currentIndex
    )
    {
        //Console.WriteLine("\n=== FIND NEXT WAYPOINT DEBUG ===");

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
                //Console.WriteLine($"[DEBUG] Already close to waypoint (dx={distanceToWaypointX}, dy={distanceToWaypointY}), resetting chase state");
                chaseTracker.CompleteReturn();
                isReturningFromChase = false;

                // Continue with normal waypoint selection below
            }
            else if (returnPosition != null)
            {
                //Console.WriteLine($"[DEBUG] Returning to chase start position: X={returnPosition.X}, Y={returnPosition.Y}, Z={returnPosition.Z}");

                // Check if we're already close to the return position
                int distanceX = Math.Abs(returnPosition.X - currentX);
                int distanceY = Math.Abs(returnPosition.Y - currentY);

                if (distanceX <= 1 && distanceY <= 1)
                {
                    //Console.WriteLine("[DEBUG] Reached chase start position, resuming normal pathing");
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
                //Console.WriteLine("[DEBUG] Chase return position is null, resetting chase state");
                chaseTracker.CompleteReturn();
                isReturningFromChase = false;
            }
        }

        //Console.WriteLine($"Current position: X={currentX}, Y={currentY}, Z={currentZ}");
        //Console.WriteLine($"Current index: {currentIndex}");
        //Console.WriteLine($"Total waypoints: {waypoints.Count}");

        if (waypoints.Count == 0)
        {
            //Console.WriteLine("ERROR: Empty waypoints list");
            return new Coordinate { X = currentX, Y = currentY, Z = currentZ };
        }

        if (currentIndex < 0 || currentIndex >= waypoints.Count)
        {
            //Console.WriteLine($"ERROR: Current index {currentIndex} out of bounds");
            currentIndex = Math.Max(0, Math.Min(waypoints.Count - 1, currentIndex));
            //Console.WriteLine($"Corrected to index {currentIndex}");
        }

        // Check if we're at the last waypoint
        if (currentIndex == waypoints.Count - 1)
        {
            //Console.WriteLine("At last waypoint, checking distance to first waypoint...");
            int distanceToFirst =
                Math.Abs(waypoints[0].X - currentX) + Math.Abs(waypoints[0].Y - currentY);
            //Console.WriteLine($"Distance to first waypoint: {distanceToFirst} steps");

            if (distanceToFirst > 12)
            {
                //Console.WriteLine("Distance > 12, REVERSING THE LIST NOW");
                waypoints.Reverse();
                currentIndex = 0;
                //Console.WriteLine("List reversed, now at index 0");
            }
            else
            {
                //Console.WriteLine("Distance <= 12, RESETTING to beginning of list");
                currentIndex = 0;
                //Console.WriteLine("Reset to index 0");
            }
        }

        int maxSearchCount = 10;
        int maxAllowedX = 5;
        int maxAllowedY = 5;
        int bestIndex = -1;
        double maxDistance = 0;
        //Console.WriteLine("Starting waypoint search...");

        int startIndex = currentIndex + 1;
        // If we've wrapped around to the beginning
        if (startIndex >= waypoints.Count)
        {
            startIndex = 0;
        }

        int endIndex = Math.Min(waypoints.Count - 1, startIndex + maxSearchCount - 1);
        //Console.WriteLine($"Searching from index {startIndex} to {endIndex}");

        for (int index = startIndex; index <= endIndex; index++)
        {
            if (index < 0 || index >= waypoints.Count)
                continue;

            Coordinate waypoint = waypoints[index];
            int deltaX = Math.Abs(waypoint.X - currentX);
            int deltaY = Math.Abs(waypoint.Y - currentY);
            //Console.WriteLine(
            ////$"  Checking waypoint[{index}]: X={waypoint.X}, Y={waypoint.Y}, deltaX={deltaX}, deltaY={deltaY}"
            ////);

            if (deltaX <= maxAllowedX && deltaY <= maxAllowedY)
            {
                double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                //Console.WriteLine($"  Distance: {distance:F2} (within limits)");

                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    bestIndex = index;
                    //Console.WriteLine($"  New best! Distance={distance:F2}, Index={bestIndex}");
                }
            }
            else
            {
                //Console.WriteLine($"  Skipped: Outside distance limits");
            }
        }

        if (bestIndex == -1)
        {
            //Console.WriteLine("No valid waypoint found ahead. Choosing adjacent waypoint...");

            // If we're at the last waypoint, we've already reset the index above
            if (currentIndex < waypoints.Count - 1)
            {
                bestIndex = currentIndex + 1;
                //Console.WriteLine($"Moving to next waypoint (index {bestIndex})");
            }
            else
            {
                bestIndex = 0; // Reset to beginning 
                //Console.WriteLine("At last waypoint, resetting to index 0");
            }
        }

        bestIndex = Math.Max(0, Math.Min(waypoints.Count - 1, bestIndex));
        currentIndex = bestIndex;
        Coordinate result = waypoints[bestIndex];

        // Apply randomization to the waypoint if enabled
        if (waypointRandomizationEnabled)
        {
            Coordinate originalResult = result;

            // Create a list of potential randomized positions to try
            List<Coordinate> potentialRandomPositions = new List<Coordinate>();

            // Try up to 5 different random variations
            for (int i = 0; i < 5; i++)
            {
                // Add random variation between -randomizationRange and +randomizationRange
                int randomX = result.X + random.Next(-randomizationRange, randomizationRange + 1);
                int randomY = result.Y + random.Next(-randomizationRange, randomizationRange + 1);

                int deltaX = Math.Abs(randomX - currentX);
                int deltaY = Math.Abs(randomY - currentY);

                // Check that the randomized position is:
                // 1. Within clickable range (5 units)
                // 2. Not a previously failed position
                if (deltaX <= 5 && deltaY <= 5)
                {
                    // Get client rect for screen coordinates
                    GetClientRect(targetWindow, out RECT rect);
                    int baseX = (rect.Right - rect.Left) / 2 - 186;
                    int baseY = (rect.Bottom - rect.Top) / 2 - baseYOffset;

                    // Calculate screen coordinates for this potential point
                    int diffX = randomX - currentX;
                    int diffY = randomY - currentY;
                    int screenX = baseX + (diffX * pixelSize);
                    int screenY = baseY + (diffY * pixelSize);

                    // Check if this point is in the failed points list
                    Point screenPoint = new Point(screenX, screenY);
                    bool isFailedPoint = false;

                    foreach (var failedPoint in failedClickPoints)
                    {
                        if (Math.Abs(failedPoint.X - screenX) < 5 && Math.Abs(failedPoint.Y - screenY) < 5)
                        {
                            isFailedPoint = true;
                            break;
                        }
                    }

                    // If it's not a failed point, add it to our potential positions
                    if (!isFailedPoint)
                    {
                        potentialRandomPositions.Add(new Coordinate
                        {
                            X = randomX,
                            Y = randomY,
                            Z = result.Z // Keep the same Z coordinate (level)
                        });
                    }
                }
            }

            // If we have any valid random positions, pick one randomly
            if (potentialRandomPositions.Count > 0)
            {
                // Choose a random position from our valid options
                int randomIndex = random.Next(potentialRandomPositions.Count);
                result = potentialRandomPositions[randomIndex];
                //Console.WriteLine($"  RANDOMIZED WAYPOINT: Original({originalResult.X},{originalResult.Y}) → Random({result.X},{result.Y})");
            }
            else
            {
                // If no valid random positions found, use the original waypoint
                //Console.WriteLine($"  No valid randomized positions found, using original waypoint ({result.X},{result.Y})");
            }
        }

        //Console.WriteLine(
        ////$"Final choice: waypoint[{bestIndex}]: X={result.X}, Y={result.Y}, Z={result.Z}"
        ////);
        //Console.WriteLine("=== END FIND NEXT WAYPOINT DEBUG ===\n");
        return result;
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


    static bool smallWindow = true;
    static bool previousSmallWindowValue = true; // Track the previous state

    static int pixelSize;
    static int baseYOffset;
    static int inventoryX;
    static int inventoryY;
    static int equipmentX;
    static int equipmentY;
    static int firstSlotBpX;
    static int firstSlotBpY;
    static int secondSlotBpX;
    static int secondSLotBpY;
    static int closeCorpseX;
    static int closeCorpseY;
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
        (800, 360),
        (800+pixelSize, 360),
        (800+2*pixelSize, 360),
        (800+3*pixelSize, 360),
        (800, 400),
        (800+pixelSize, 400),
        (800+2*pixelSize, 400),
        (800+3*pixelSize, 400)
    };
    static (int, int)[] GetCorspeFoodCoordinates()
    {
        return smallWindow ? smallCoordinates : normalCoordinates;
    }

    static void UpdateUIPositions()
    {
        GetClientRect(targetWindow, out RECT windowRect);
        int windowHeight = windowRect.Bottom - windowRect.Top;
        //Console.WriteLine($"[DEBUG] Detected window height: {windowHeight}px");
        smallWindow = windowHeight < 1200;
        //Console.WriteLine($"[DEBUG] Using {(smallWindow ? "small window (1080p)" : "large window (1440p)")} settings");

        bool valueChanged = (previousSmallWindowValue != smallWindow);
        previousSmallWindowValue = smallWindow;

        pixelSize = smallWindow ? 38 : 58;
        baseYOffset = smallWindow ? 260 : 300;
        inventoryX = smallWindow ? 620 : 940;
        inventoryY = 65;
        equipmentX = smallWindow ? 800 : 1115;
        equipmentY = 150;
        secondSlotBpX = smallWindow ? 840 : 1165;
        secondSLotBpY = 250;
        firstSlotBpX = smallWindow ? 840 - pixelSize : 1165 - pixelSize;
        firstSlotBpY = 250;
        closeCorpseX = smallWindow ? 944 : 1262;
        closeCorpseY = smallWindow ? 320 : 400;

        {
            //Console.WriteLine($"[DEBUG] Window size changed to: {(smallWindow ? "small (1080p)" : "large (1440p)")}");
            //Console.WriteLine($"[DEBUG] UI positions updated:");
            //Console.WriteLine($"  - Pixel size: {pixelSize}");
            //Console.WriteLine($"  - Base Y Offset: {baseYOffset}");
            //Console.WriteLine($"  - Inventory position: ({inventoryX},{inventoryY})");
            //Console.WriteLine($"  - Equipment position: ({equipmentX},{equipmentY})");
            //Console.WriteLine($"  - Second slot position: ({secondSlotBpX},{secondSLotBpY})");
        }

        if (threadFlags["spawnwatch"] && SPAWNWATCHER.IsActive())
        {
            SPAWNWATCHER.UpdateScanArea(targetWindow, pixelSize);
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
    static void CloseCorpse(IntPtr hWnd)
    {
        (int x, int y)[] locations = new (int, int)[] { (closeCorpseX, closeCorpseY) };
        GetClientRect(hWnd, out RECT rect);
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

    static int maxItemsToEat = 2;
    static void CorpseEatFood(IntPtr hWnd)
    {
        return;
        (int x, int y)[] locations = GetCorspeFoodCoordinates();
        GetClientRect(hWnd, out RECT rect);
        if (rect.Right < 1237 || rect.Bottom < 319)
        {
            // Calculate how many items to process (either maxItemsToEat or all available items)
            int itemsToProcess = Math.Min(maxItemsToEat, locations.Length);

            for (int i = 0; i < itemsToProcess; i++)
            {
                int x = locations[i].x;
                int y = locations[i].y;
                POINT screenPoint = new POINT { X = x, Y = y };
                ClientToScreen(hWnd, ref screenPoint);
                Sleep(1);
                VirtualRightClick(hWnd, x, y);
                Sleep(1);
            }
        }
    }
    static void ClickSecondSlotInBackpack(IntPtr hWnd)
    {
        return;
        (int x, int y)[] locations = new (int, int)[] { (secondSlotBpX, secondSLotBpY) };

        GetClientRect(hWnd, out RECT rect);
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
    const int MK_LBUTTON = 0x0001;
    static IntPtr MakeLParam(int x, int y)
    {
        return (IntPtr)((y << 16) | (x & 0xFFFF));
    }
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

    static bool isClickAroundInProgress = false;
    static DateTime lastClickAroundCompleted = DateTime.MinValue;
    static int lastClickAroundTargetId = 0;

    static bool ClickAroundCharacter(IntPtr hWnd)
    {
        try
        {
            // Set flag to indicate click around is in progress
            isClickAroundInProgress = true;
            //Console.WriteLine("[DEBUG] Click around character operation started");

            // Get last known target coordinates and player position
            int currentX, currentY, currentZ;
            int monsterX = 0, monsterY = 0, monsterZ = 0;

            lock (memoryLock)
            {
                currentX = posX;
                currentY = posY;
                currentZ = posZ;

                // Get the last known target position
                monsterX = lastKnownMonsterX;
                monsterY = lastKnownMonsterY;
                monsterZ = lastKnownMonsterZ;
            }

            //Console.WriteLine($"[DEBUG] Player position: X={currentX}, Y={currentY}, Z={currentZ}");
            //Console.WriteLine($"[DEBUG] Last monster position: X={monsterX}, Y={monsterY}, Z={monsterZ}");

            // Get window dimensions for center calculation
            GetClientRect(hWnd, out RECT rect);
            int screenCenterX = (rect.Right - rect.Left) / 2 - 186;
            int screenCenterY = (rect.Bottom - rect.Top) / 2 - baseYOffset;

            //Console.WriteLine($"[DEBUG] Screen center: X={screenCenterX}, Y={screenCenterY}");

            // Define all directions for clicking in exact clockwise order
            (int dx, int dy)[] clickPattern = new[]
            {
            (0, 0),   // Center (where the player is standing)
            (0, -1),  // North
            (1, -1),  // Northeast
            (1, 0),   // East
            (1, 1),   // Southeast
            (0, 1),   // South
            (-1, 1),  // Southwest
            (-1, 0),  // West
            (-1, -1)  // Northwest
        };

            // Calculate relative position of monster to player
            // If monster is directly on a specific direction, we'll move that direction to the front
            (int dx, int dy) monsterRelativePos = (0, 0);
            if (monsterX != 0 || monsterY != 0)
            {
                // Calculate relative direction (simple subtraction)
                int relX = monsterX - currentX;
                int relY = monsterY - currentY;

                //Console.WriteLine($"[DEBUG] Relative monster position: ({relX}, {relY})");

                // Find closest direction in our click pattern
                if (relX != 0 || relY != 0)
                {
                    // Normalize to -1, 0, or 1 values
                    monsterRelativePos = (
                        Math.Max(-1, Math.Min(1, relX)),
                        Math.Max(-1, Math.Min(1, relY))
                    );

                    //Console.WriteLine($"[DEBUG] Normalized monster direction: ({monsterRelativePos.dx}, {monsterRelativePos.dy})");
                }
            }

            // Reorder click pattern to start at monster's position if we have one
            List<(int dx, int dy)> orderedClickPattern = new List<(int dx, int dy)>();

            // First add monster position if valid
            if (monsterRelativePos.dx != 0 || monsterRelativePos.dy != 0)
            {
                orderedClickPattern.Add(monsterRelativePos);
                //Console.WriteLine($"[DEBUG] First click will be at monster position: ({monsterRelativePos.dx}, {monsterRelativePos.dy})");
            }

            // Then add all other positions in clockwise order
            foreach (var pos in clickPattern)
            {
                // Skip the monster position since we already added it
                if (pos != monsterRelativePos)
                {
                    orderedClickPattern.Add(pos);
                }
            }

            //Console.WriteLine("[DEBUG] Final click pattern:");
            for (int i = 0; i < orderedClickPattern.Count; i++)
            {
                //Console.WriteLine($"[DEBUG]   {i + 1}: ({orderedClickPattern[i].dx}, {orderedClickPattern[i].dy})");
            }

            // Add a delay before clicking to allow overlays to initialize
            Sleep(1);

            // Click in each direction according to our pattern
            foreach (var direction in orderedClickPattern)
            {
                int dx = direction.dx;
                int dy = direction.dy;

                // Calculate absolute screen coordinates
                int clickX = screenCenterX + (dx * pixelSize);
                int clickY = screenCenterY + (dy * pixelSize);

                //Console.WriteLine($"[DEBUG] Clicking at screen position: X={clickX}, Y={clickY} (offset: {dx}, {dy})");

                Sleep(1); // Give time for highlight to appear
                VirtualRightClick(targetWindow, clickX, clickY);
                Sleep(1); // Allow game time to process click

                // Check if we found a target
                int currentTargetId;
                lock (memoryLock)
                {
                    currentTargetId = targetId;
                }

                if (currentTargetId != 0)
                {
                    //Console.WriteLine($"[DEBUG] Target acquired during click around. Target ID: {currentTargetId}");
                    lastClickAroundTargetId = currentTargetId; // Track the target ID we found
                    lastClickAroundCompleted = DateTime.Now;

                    // Additional looting operations since we found a target
                    CorpseEatFood(targetWindow);
                    Sleep(1);
                    ClickSecondSlotInBackpack(hWnd);
                    Sleep(1);

                    isClickAroundInProgress = false;
                    return true; // Found target
                }
            }

            // Additional looting operations
            CorpseEatFood(targetWindow);
            Sleep(1); // Allow time for food looting
            ClickSecondSlotInBackpack(hWnd);
            Sleep(1); // Allow time for backpack operation

            // Play a sound to indicate click around is complete
            if (typeof(Program).GetMethod("PlayClickCompletedSound") != null)
            {
                PlayClickCompletedSound();
            }

            //Console.WriteLine("[DEBUG] Click around character operation completed");
            lastClickAroundCompleted = DateTime.Now;
            isClickAroundInProgress = false;
            return false; // No target found
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Error in ClickAroundCharacter: {ex.Message}");
            isClickAroundInProgress = false;
            return false;
        }
    }

    const int WM_MOUSEMOVE = 0x0200;
    const int WM_RBUTTONDOWN = 0x0204;
    const int WM_RBUTTONUP = 0x0205;
    const uint VK_LCONTROL = 0xA2;
    const uint VK_LMENU = 0xA4;
    const uint KEYEVENTF_KEYUP = 0x0002;
    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);
    static void VirtualLeftClick(IntPtr hWnd, int x, int y)
    {
        int lParam = (y << 16) | (x & 0xFFFF);

        SendMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, (IntPtr)lParam);
        Sleep(1);
        SendMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)1, (IntPtr)lParam);
        Sleep(1);
        SendMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, (IntPtr)lParam);
        Sleep(1);

        // Record the click position
        RecordClickPosition(x, y, true);
    }

    static void VirtualRightClick(IntPtr hWnd, int x, int y)
    {
        int lParam = (y << 16) | (x & 0xFFFF);

        SendMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, (IntPtr)lParam);
        SendMessage(hWnd, WM_RBUTTONDOWN, (IntPtr)1, (IntPtr)lParam);
        SendMessage(hWnd, WM_RBUTTONUP, IntPtr.Zero, (IntPtr)lParam);

        // Record the click position
        RecordClickPosition(x, y, false);
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
            if (monsterX != 0) lastKnownMonsterX = monsterX;
            if (monsterY != 0) lastKnownMonsterY = monsterY;
            if (monsterZ != 0) lastKnownMonsterZ = monsterZ;
            if (targetId != 0) lastKnownMonsterId = targetId;
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
    static List<string> whitelistedMonsterNames = new List<string> {
        "Tarantula",
        "Poison Spider",
        "Frost Giant",
        "Frost Giantess",
        "Amazon",
        "Valkryie"
    };

    //    static List<string> whitelistedMonsterNames = new List<string> {
    //    "Tarantula",
    //    "Poison Spider",
    //    "Centipede",
    //    "Dworc Venomsniper",
    //    "Dworc Fleshhunter",
    //    "Dworc Voodoomaster",
    //    "Crypt Shambler"
    //};

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
            if (monsterX != 0) lastKnownMonsterX = monsterX;
            if (monsterY != 0) lastKnownMonsterY = monsterY;
            if (monsterZ != 0) lastKnownMonsterZ = monsterZ;
            if (targetId != 0) lastKnownMonsterId = targetId;
        }

        if (!string.IsNullOrEmpty(monsterName) && monsterName != "" && !whitelistedMonsterNames.Contains(monsterName))
        {
            //Console.WriteLine($"[CRITICAL] Non-whitelisted monster detected: '{monsterName}'. Stopping all threads!");

            // Stop all threads
            threadFlags["recording"] = false;
            threadFlags["playing"] = false;
            threadFlags["autopot"] = false;
            memoryReadActive = false;

            // Start the 60-second emergency alert
            PlayEmergencyAlert(60);

            // Consider adding this to force the program to exit completely after the alert
            Environment.Exit(1);
        }


        return (monsterX, monsterY, monsterZ, monsterName);
    }

    static void PlayEmergencyAlert(int durationSeconds)
    {
        //Console.WriteLine($"[EMERGENCY] Playing alert for {durationSeconds} seconds!");

        // Create a dedicated thread for the alert that won't be stopped by the other thread controls
        Thread alertThread = new Thread(() =>
        {
            DateTime endTime = DateTime.Now.AddSeconds(durationSeconds);

            try
            {
                while (DateTime.Now < endTime)
                {
                    // Use a more urgent, higher-pitched sound for the emergency
                    Beep(2000, 300);  // Higher frequency
                    Thread.Sleep(100);
                    Beep(1800, 300);
                    Thread.Sleep(100);
                    Beep(2000, 300);

                    // Small pause between alert sequences
                    Thread.Sleep(500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in emergency alert: {ex.Message}");
            }
        });

        // Make sure this thread will continue even if the main thread ends
        alertThread.IsBackground = false;
        alertThread.Start();

        // Wait for the alert to finish
        alertThread.Join();
    }

    static bool withLifeRing = true;
    static bool onceLifeRing = true;

    static int lastRingEquippedTargetId = 0;
    static bool isRingCurrentlyEquipped = false;
    static List<string> blacklistedRingMonsters = new List<string> { "Poison Spider", };
    static readonly int MAX_MONSTER_DISTANCE = 4; // Maximum allowed distance in sqm
    static void ToggleRing(IntPtr hWnd, bool equip)
    {
        ScanRingContainersForMisplacedRings();
        try
        {
            int currentTargetId;
            int invisiblityCodeVar;
            lock (memoryLock)
            {
                currentTargetId = targetId;
                invisiblityCodeVar = invisibilityCode;
            }

            // Check invisibilityCode to determine current ring state
            // invisibilityCode = 2 means the ring is already equipped
            bool isRingEquipped = invisiblityCodeVar == 2;

            // If we want to equip and it's already equipped, or want to de-equip and it's not equipped, do nothing
            if ((equip && isRingEquipped) || (!equip && !isRingEquipped))
            {
                return;
            }



            if (equip && isRingEquipped == false && withLifeRing && currentTargetId != 0)
            {
                int lifeRingX = inventoryX;
                int lifeRingY = inventoryY + 3 * pixelSize + 15;

                //Console.WriteLine("[DEBUG] Moving life ring back to inventory");

                // Perform drag-and-drop to move life ring back to inventory
                IntPtr lifeRingSourceLParam = MakeLParam(equipmentX, equipmentY);
                SendMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, lifeRingSourceLParam);
                Sleep(1);
                SendMessage(hWnd, WM_LBUTTONDOWN, IntPtr.Zero, lifeRingSourceLParam);
                Sleep(1);
                // Record the source click for our overlay
                RecordClickPosition(equipmentX, equipmentY, true);

                // Destination is life ring inventory slot
                IntPtr lifeRingDestLParam = MakeLParam(lifeRingX, lifeRingY);
                SendMessage(hWnd, WM_MOUSEMOVE, new IntPtr(MK_LBUTTON), lifeRingDestLParam);
                Sleep(1);
                SendMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, lifeRingDestLParam);
                Sleep(1);
                // Record the destination click for our overlay
                RecordClickPosition(lifeRingX, lifeRingY, true);

                //Console.WriteLine("[DEBUG] Successfully moved life ring back to inventory");
                //Sleep(100); // Add a small delay before continuing with normal ring
                //Sleep(4000);
                Sleep(1);
            }

            Sleep(512);
            // Check blacklisted monsters if trying to equip
            if (equip && currentTargetId != 0)
            {
                var (monsterX, monsterY, monsterZ, monsterName) = GetTargetMonsterInfo();
                if (!string.IsNullOrEmpty(monsterName) && blacklistedRingMonsters.Contains(monsterName))
                {
                    //Console.WriteLine($"[DEBUG] Monster '{monsterName}' is blacklisted for ring usage");
                    return;
                }

                int currentX, currentY;
                lock (memoryLock)
                {
                    currentX = posX;
                    currentY = posY;
                }

                int distanceX = Math.Abs(monsterX - currentX);
                int distanceY = Math.Abs(monsterY - currentY);
                int totalDistance = distanceX + distanceY;

                // If monster is too far away and we're not moving
                if (totalDistance > MAX_MONSTER_DISTANCE)
                {
                    return;
                }
            }

            // Set source and destination coordinates based on whether we're equipping or de-equipping
            int sourceX = equip ? inventoryX : equipmentX;
            int sourceY = equip ? inventoryY : equipmentY;
            int destX = equip ? equipmentX : inventoryX;
            int destY = equip ? equipmentY : inventoryY;

            //Console.WriteLine($"[DEBUG] {(equip ? "Equipping" : "De-equipping")} ring");

            // Perform the drag-and-drop operation
            IntPtr sourceLParam = MakeLParam(sourceX, sourceY);
            SendMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, sourceLParam);
            Sleep(25);
            SendMessage(hWnd, WM_LBUTTONDOWN, IntPtr.Zero, sourceLParam);
            Sleep(25);

            // *** Record the source click for our overlay ***
            RecordClickPosition(sourceX, sourceY, true);

            IntPtr destLParam = MakeLParam(destX, destY);
            SendMessage(hWnd, WM_MOUSEMOVE, new IntPtr(MK_LBUTTON), destLParam);
            Sleep(25);
            SendMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, destLParam);
            Sleep(1);

            // *** Record the destination click for our overlay ***
            RecordClickPosition(destX, destY, true);

            onceLifeRing = true;

            if (!equip && withLifeRing && onceLifeRing && currentTargetId == 0)
            {
                Sleep(128);
                onceLifeRing = false;
                // Calculate life ring position - same X as inventory ring but 2 pixels down
                int lifeRingX = inventoryX;
                int lifeRingY = inventoryY + 3 * pixelSize + 15;

                //Console.WriteLine("[DEBUG] Equipping life ring");
                double xd = 9999999;
                lock (memoryLock)
                {
                    xd = curMana;
                }
                if (xd < 845)
                {
                    IntPtr lifeRingSourceLParam = MakeLParam(lifeRingX, lifeRingY);
                    SendMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, lifeRingSourceLParam);
                    Sleep(25);
                    SendMessage(hWnd, WM_LBUTTONDOWN, IntPtr.Zero, lifeRingSourceLParam);
                    Sleep(25);
                    // Record the source click for our overlay
                    RecordClickPosition(lifeRingX, lifeRingY, true);

                    // Destination is equipment slot
                    IntPtr lifeRingDestLParam = MakeLParam(equipmentX, equipmentY);
                    SendMessage(hWnd, WM_MOUSEMOVE, new IntPtr(MK_LBUTTON), lifeRingDestLParam);
                    Sleep(25);
                    SendMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, lifeRingDestLParam);
                    Sleep(1);
                    // Record the destination click for our overlay
                    RecordClickPosition(equipmentX, equipmentY, true);
                }
                //Console.WriteLine("[DEBUG] Successfully equipped life ring");
            }

            // Log the result of the operation
            if (equip)
            {
                //Console.WriteLine($"[DEBUG] Successfully equipped ring for target {currentTargetId}");
            }
            else
            {
                //Console.WriteLine($"[DEBUG] Successfully de-equipped ring");
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
                //Console.WriteLine("Warning: click_completed.wav not found. Using system beep instead.");
            }
            else
            {
                //Console.WriteLine("Found click_completed.wav");
            }

            if (!File.Exists(positionAlertSoundPath))
            {
                //Console.WriteLine("Warning: position_alert.wav not found. Using system beep instead.");
            }
            else
            {
                //Console.WriteLine("Found position_alert.wav");
            }

            //Console.WriteLine("Sound system initialized successfully.");

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
                    //Console.WriteLine("[SOUND] Click around completed sound played from file.");
                    return;
                }
            }

            // Fall back to Beep if file doesn't exist or PlaySound failed
            Beep(CLICK_COMPLETED_FREQ, CLICK_COMPLETED_DURATION);
            //Console.WriteLine("[SOUND] Click around completed beep played.");
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

            //Console.WriteLine($"[SOUND] Starting position alert sound (max {maxDurationSeconds} seconds).");
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

                    //Console.WriteLine("[SOUND] Position alert sound stopped.");
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
                //Console.WriteLine("[SOUND] Position alert sound cancelled.");
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
            //Console.WriteLine($"[WARNING] Position too far from any waypoint! Distance: {minDistance} sqm");
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
                //Console.WriteLine(message);

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
            //Console.WriteLine($"[DEBUG] Removed {expiredTargets.Count} expired blacklisted targets");
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
                //Console.WriteLine($"[DEBUG] Target monster (ID: {currentTargetId}) is too far away ({totalDistance} sqm) and player is stuck. Blacklisting for {TARGET_BLACKLIST_DURATION.TotalSeconds} seconds.");

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
        //Console.WriteLine("\nBlacklisted Targets:");
        //Console.WriteLine(new string('-', 40));

        foreach (var kvp in blacklistedTargetTimers)
        {
            TimeSpan remainingTime = kvp.Value - now;
            if (remainingTime.TotalSeconds > 0)
            {
                //Console.WriteLine($"  Target ID: {kvp.Key}, Time remaining: {remainingTime.TotalSeconds:F1} seconds");
            }
        }
    }
    static class SPAWNWATCHER
    {
        private static string imageFolderPath = "images";
        private static bool watcherActive = false;
        private static Thread watcherThread = null;
        private static object watcherLock = new object();
        private static double matchThreshold = 0.84; // Adjust this based on testing
        private static bool verboseDebug = false;
        private static long scanCount = 0;
        private static long totalScanTime = 0;
        private static DateTime lastDebugOutput = DateTime.MinValue;
        private static readonly TimeSpan debugOutputInterval = TimeSpan.FromSeconds(3);
        private static int scanCenterX = 0;
        private static int scanCenterY = 0;
        private static int scanWidth = 0;
        private static int scanHeight = 0;

        // Color detection settings
        private static bool colorDetectionEnabled = true;
        private static DateTime lastColorAlarmTime = DateTime.MinValue;
        private static readonly TimeSpan colorAlarmCooldown = TimeSpan.FromSeconds(60); // 60 second cooldown between color alarms

        // Last detected match timestamp to implement cooldown
        private static DateTime lastMatchTime = DateTime.MinValue;
        private static readonly TimeSpan matchCooldown = TimeSpan.FromSeconds(30); // 30 second cooldown between matches

        // Color definitions (HSV ranges)
        private static MCvScalar redLowerBound1 = new MCvScalar(0, 100, 100, 0);
        private static MCvScalar redUpperBound1 = new MCvScalar(10, 255, 255, 0);
        private static MCvScalar redLowerBound2 = new MCvScalar(160, 100, 100, 0);
        private static MCvScalar redUpperBound2 = new MCvScalar(180, 255, 255, 0);
        private static MCvScalar yellowLowerBound = new MCvScalar(20, 100, 100, 0);
        private static MCvScalar yellowUpperBound = new MCvScalar(40, 255, 255, 0);
        private static MCvScalar blueLowerBound = new MCvScalar(100, 100, 100, 0);
        private static MCvScalar blueUpperBound = new MCvScalar(130, 255, 255, 0);

        // Class to store template information
        private class TemplateInfo
        {
            public string FilePath { get; set; }
            public Mat Template { get; set; }
            public string Name => Path.GetFileNameWithoutExtension(FilePath);
            public int Width => Template?.Width ?? 0;
            public int Height => Template?.Height ?? 0;
            public int Priority { get; set; } = 0; // Default priority is 0
        }

        private static List<TemplateInfo> templates = new List<TemplateInfo>();

        public static bool IsActive()
        {
            lock (watcherLock)
            {
                return watcherActive;
            }
        }

        public static string GetStats()
        {
            lock (watcherLock)
            {
                if (!watcherActive || scanCount == 0)
                    return "No stats available";
                double avgScanTimeMs = (double)totalScanTime / scanCount;
                double scansPerSecond = scanCount > 0 ? 1000.0 / avgScanTimeMs : 0;
                return $"Scans: {scanCount}, Avg time: {avgScanTimeMs:F2}ms, Rate: {scansPerSecond:F1}/sec";
            }
        }

        public static void Start(IntPtr gameWindow, int pixelSize)
        {
            lock (watcherLock)
            {
                if (watcherActive)
                {
                    //Console.WriteLine("[SPAWN] Watcher already running");
                    return;
                }

                if (!Directory.Exists(imageFolderPath))
                {
                    Directory.CreateDirectory(imageFolderPath);
                    //Console.WriteLine($"[SPAWN] Created image directory: {Path.GetFullPath(imageFolderPath)}");
                    //Console.WriteLine("[SPAWN] Place PNG or JPG template images in this directory to detect them");
                }

                // Calculate scan area for template matching
                GetClientRect(gameWindow, out RECT rect);
                scanCenterX = (rect.Right - rect.Left) / 2 - 186;
                scanCenterY = (rect.Bottom - rect.Top) / 2 - 260; // Assuming baseYOffset is 260
                scanWidth = pixelSize * 10;
                scanHeight = pixelSize * 10;

                //Console.WriteLine($"[SPAWN] Scan area set to: Center({scanCenterX},{scanCenterY}), Size({scanWidth}x{scanHeight})");
                //Console.WriteLine($"[SPAWN] Color detection: {(colorDetectionEnabled ? "Enabled" : "Disabled")}");

                LoadTemplates();

                if (templates.Count == 0)
                {
                    //Console.WriteLine($"[SPAWN] No template images found in {Path.GetFullPath(imageFolderPath)}");
                    //Console.WriteLine("[SPAWN] Add .png or .jpg files to detect them in-game");
                }
                else
                {
                    //Console.WriteLine($"[SPAWN] Loaded {templates.Count} template(s) to watch for:");
                    foreach (var template in templates)
                    {
                        //Console.WriteLine($"[SPAWN] - {template.Name} ({template.Width}x{template.Height}, Priority: {template.Priority})");
                    }
                }

                scanCount = 0;
                totalScanTime = 0;
                watcherActive = true;

                watcherThread = new Thread(() => WatcherThreadFunction(gameWindow));
                watcherThread.IsBackground = true;
                watcherThread.Name = "OptimizedSpawnWatcher";
                watcherThread.Start();

                //Console.WriteLine("[SPAWN] Optimized watcher thread started");
            }
        }

        public static void Stop()
        {
            lock (watcherLock)
            {
                if (!watcherActive)
                {
                    return;
                }

                watcherActive = false;
                //Console.WriteLine("[SPAWN] Watcher stopping...");

                if (scanCount > 0)
                {
                    double avgScanTimeMs = (double)totalScanTime / scanCount;
                    double scansPerSecond = 1000.0 / avgScanTimeMs;
                    //Console.WriteLine($"[SPAWN] Final stats: {scanCount} scans completed");
                    //Console.WriteLine($"[SPAWN] Average scan time: {avgScanTimeMs:F2}ms");
                    //Console.WriteLine($"[SPAWN] Scan rate: {scansPerSecond:F1} scans/second");
                }

                // Clean up resources
                foreach (var template in templates)
                {
                    template.Template?.Dispose();
                }
                templates.Clear();
            }
        }

        public static bool Toggle(IntPtr gameWindow, int pixelSize)
        {
            lock (watcherLock)
            {
                if (watcherActive)
                {
                    Stop();
                    return false;
                }
                else
                {
                    Start(gameWindow, pixelSize);
                    return true;
                }
            }
        }

        public static void UpdateScanArea(IntPtr gameWindow, int pixelSize)
        {
            lock (watcherLock)
            {
                if (!watcherActive)
                    return;

                GetClientRect(gameWindow, out RECT rect);
                scanCenterX = (rect.Right - rect.Left) / 2 - 186;
                scanCenterY = (rect.Bottom - rect.Top) / 2 - 260; // Assuming baseYOffset is 260
                scanWidth = pixelSize * 10;
                scanHeight = pixelSize * 10;

                //Console.WriteLine($"[SPAWN] Scan area updated: Center({scanCenterX},{scanCenterY}), Size({scanWidth}x{scanHeight})");
            }
        }

        public static void ReloadTemplates()
        {
            lock (watcherLock)
            {
                foreach (var template in templates)
                {
                    template.Template?.Dispose();
                }
                templates.Clear();

                LoadTemplates();

                if (templates.Count == 0)
                {
                    //Console.WriteLine($"[SPAWN] No template images found in {Path.GetFullPath(imageFolderPath)}");
                }
                else
                {
                    //Console.WriteLine($"[SPAWN] Reloaded {templates.Count} template(s) to watch for:");
                    foreach (var template in templates)
                    {
                        //Console.WriteLine($"[SPAWN] - {template.Name} ({template.Width}x{template.Height}, Priority: {template.Priority})");
                    }
                }
            }
        }

        private static void LoadTemplates()
        {
            try
            {
                if (!Directory.Exists(imageFolderPath))
                {
                    return;
                }

                string[] imageFiles = Directory.GetFiles(imageFolderPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => IsImageFile(f))
                    .ToArray();

                //Console.WriteLine($"[SPAWN] Found {imageFiles.Length} image files in {Path.GetFullPath(imageFolderPath)}");

                // Load template priority settings if available
                Dictionary<string, int> priorityMap = LoadTemplatePriorities();

                foreach (string filePath in imageFiles)
                {
                    try
                    {
                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        //Console.WriteLine($"[SPAWN] Loading template: {Path.GetFileName(filePath)}");

                        // Load template image directly as Mat
                        Mat template = CvInvoke.Imread(filePath, ImreadModes.Color);

                        if (template.IsEmpty)
                        {
                            //Console.WriteLine($"[SPAWN] Failed to load template: {Path.GetFileName(filePath)}");
                            continue;
                        }

                        // Get priority from map or use default
                        int priority = 0;
                        if (priorityMap.ContainsKey(fileName))
                        {
                            priority = priorityMap[fileName];
                        }

                        //Console.WriteLine($"[SPAWN] Loaded {Path.GetFileName(filePath)}: {template.Width}x{template.Height}, Priority: {priority}");

                        templates.Add(new TemplateInfo
                        {
                            FilePath = filePath,
                            Template = template,
                            Priority = priority
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SPAWN] Error loading template {filePath}: {ex.Message}");
                    }
                }

                // Sort templates by priority (higher priority first)
                templates = templates.OrderByDescending(t => t.Priority).ToList();

                //Console.WriteLine("[SPAWN] Templates sorted by priority:");
                foreach (var template in templates)
                {
                    //Console.WriteLine($"[SPAWN] - {template.Name} (Priority: {template.Priority})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SPAWN] Error loading templates: {ex.Message}");
            }
        }

        // Load template priorities from a config file
        private static Dictionary<string, int> LoadTemplatePriorities()
        {
            Dictionary<string, int> priorities = new Dictionary<string, int>();
            string priorityFilePath = Path.Combine(imageFolderPath, "priorities.txt");

            if (!File.Exists(priorityFilePath))
            {
                //Console.WriteLine("[SPAWN] No priorities.txt file found. Using default priorities.");
                //Console.WriteLine("[SPAWN] To set priorities, create a file 'images/priorities.txt' with lines like 'image_name.png=10'");
                return priorities;
            }

            try
            {
                string[] lines = File.ReadAllLines(priorityFilePath);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    string[] parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        string imageName = parts[0].Trim();
                        if (int.TryParse(parts[1].Trim(), out int priority))
                        {
                            priorities[imageName] = priority;
                            //Console.WriteLine($"[SPAWN] Priority set: {imageName} = {priority}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SPAWN] Error loading priorities: {ex.Message}");
            }

            return priorities;
        }

        private static bool IsImageFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";
        }

        private static unsafe Mat CaptureWindowAsMat(IntPtr hWnd, int x, int y, int width, int height)
        {
            IntPtr hdcWindow = IntPtr.Zero;
            IntPtr hdcMemDC = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr hOld = IntPtr.Zero;
            Mat result = null;

            try
            {
                hdcWindow = GetDC(hWnd);
                if (hdcWindow == IntPtr.Zero)
                {
                    if (verboseDebug)
                        //Console.WriteLine("[SPAWN] GetDC failed");
                        return null;
                }

                hdcMemDC = CreateCompatibleDC(hdcWindow);
                if (hdcMemDC == IntPtr.Zero)
                {
                    if (verboseDebug)
                        //Console.WriteLine("[SPAWN] CreateCompatibleDC failed");
                        return null;
                }

                hBitmap = CreateCompatibleBitmap(hdcWindow, width, height);
                if (hBitmap == IntPtr.Zero)
                {
                    if (verboseDebug)
                        //Console.WriteLine("[SPAWN] CreateCompatibleBitmap failed");
                        return null;
                }

                hOld = SelectObject(hdcMemDC, hBitmap);

                bool success = BitBlt(hdcMemDC, 0, 0, width, height, hdcWindow, x, y, SRCCOPY);
                if (!success)
                {
                    if (verboseDebug)
                        //Console.WriteLine("[SPAWN] BitBlt failed");
                        return null;
                }

                SelectObject(hdcMemDC, hOld);

                // Create a temporary Bitmap and convert to Mat
                using (Bitmap bmp = Bitmap.FromHbitmap(hBitmap))
                {
                    // This is much faster than using Image<Bgr, byte>
                    Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                    BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

                    try
                    {
                        // Create a Mat directly from the bitmap data - no copying
                        result = new Mat(bmp.Height, bmp.Width, DepthType.Cv8U, 3, bmpData.Scan0, bmpData.Stride);
                        // Create a deep copy to keep the Mat alive after unlocking the bitmap
                        result = result.Clone();
                    }
                    finally
                    {
                        bmp.UnlockBits(bmpData);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SPAWN] Screenshot error: {ex.Message}");
                result?.Dispose();
                return null;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                if (hdcMemDC != IntPtr.Zero) DeleteDC(hdcMemDC);
                if (hdcWindow != IntPtr.Zero) ReleaseDC(hWnd, hdcWindow);
            }
        }

        static void WatcherThreadFunction(IntPtr gameWindow)
        {
            //Console.WriteLine("[SPAWN] Optimized watcher thread started");

            try
            {
                Stopwatch iterationTimer = new Stopwatch();
                int iterationCount = 0;
                DateTime lastFullScanTime = DateTime.MinValue;

                // Reduce frequency of template matching
                const int TEMPLATE_MATCHING_INTERVAL_MS = 1000; // Once per second instead of continuous

                // Create and reuse result matrices for template matching
                Mat resultMat = new Mat();

                while (watcherActive)
                {
                    try
                    {
                        iterationTimer.Restart();
                        iterationCount++;
                        DateTime now = DateTime.Now;

                        // Get window dimensions for both template matching and color detection
                        GetClientRect(gameWindow, out RECT clientRect);
                        int windowWidth = clientRect.Right - clientRect.Left;
                        int windowHeight = clientRect.Bottom - clientRect.Top;

                        // Only run template matching periodically
                        bool shouldDoFullScan = (now - lastFullScanTime).TotalMilliseconds >= TEMPLATE_MATCHING_INTERVAL_MS;

                        if (!shouldDoFullScan)
                        {
                            // Just a short sleep when not doing anything to prevent CPU hammering
                            Thread.Sleep(100);
                            continue;
                        }

                        lastFullScanTime = now;

                        // Determine if we should run template matching
                        bool doTemplateMatching = templates.Count > 0 &&
                                                  (DateTime.Now - lastMatchTime) >= matchCooldown;

                        // Determine if we should run color detection
                        bool doColorDetection = colorChangeAlarmEnabled &&
                                                  (DateTime.Now - lastColorAlarmTime) >= colorAlarmCooldown;

                        // If nothing to do, wait a bit and continue
                        if (!doTemplateMatching && !doColorDetection)
                        {
                            Thread.Sleep(100);
                            continue;
                        }

                        // =================== TEMPLATE MATCHING ===================
                        if (doTemplateMatching)
                        {
                            int scanLeft = Math.Max(0, scanCenterX - (scanWidth / 2));
                            int scanTop = Math.Max(0, scanCenterY - (scanHeight / 2));

                            // Output debug info occasionally
                            if (verboseDebug && DateTime.Now.Subtract(lastDebugOutput) > debugOutputInterval)
                            {
                                //Console.WriteLine($"[SPAWN] Scanning area: X={scanLeft}-{scanLeft + scanWidth}, Y={scanTop}-{scanTop + scanHeight}");
                                lastDebugOutput = DateTime.Now;
                            }

                            // Capture the screen area as a Mat
                            using (Mat screenshot = CaptureWindowAsMat(gameWindow, scanLeft, scanTop, scanWidth, scanHeight))
                            {
                                if (screenshot == null)
                                {
                                    if (verboseDebug && iterationCount % 100 == 0)
                                    {
                                        //Console.WriteLine("[SPAWN] Failed to capture screenshot");
                                    }
                                }
                                else
                                {
                                    bool debugOutputThisIteration = verboseDebug && iterationCount % 50 == 0;
                                    if (debugOutputThisIteration)
                                    {
                                        //Console.WriteLine($"[SPAWN] Scan #{scanCount + 1}: Comparing against {templates.Count} templates in priority order...");
                                    }

                                    bool matchFound = false;

                                    // Process templates in priority order (single-threaded)
                                    foreach (var template in templates)
                                    {
                                        double minVal = 0, maxVal = 0;
                                        Point minLoc = new Point(), maxLoc = new Point();

                                        // Perform template matching with result mat reuse
                                        CvInvoke.MatchTemplate(screenshot, template.Template, resultMat, TemplateMatchingType.CcoeffNormed);
                                        CvInvoke.MinMaxLoc(resultMat, ref minVal, ref maxVal, ref minLoc, ref maxLoc);
                                        bool match = maxVal >= matchThreshold;

                                        if (debugOutputThisIteration || maxVal > 0.5)
                                        {
                                            //Console.WriteLine($"[SPAWN] Template {template.Name} (Priority: {template.Priority}): similarity={maxVal:F3} at ({maxLoc.X},{maxLoc.Y}) - {(match ? "MATCH!" : "no match")}");
                                        }

                                        if (match)
                                        {
                                            Rectangle matchedRect = new Rectangle(maxLoc, new Size(template.Width, template.Height));
                                            Mat matchedRegion = new Mat(screenshot, matchedRect);

                                            var (isColorSimilar, colorSimilarityPercent) = IsColorSimilar(template.Template, matchedRegion);

                                            if (isColorSimilar)
                                            {
                                                //Console.WriteLine($"[SPAWN] MATCH FOUND: {template.Name}, Shape Match: {maxVal:F3}, Color Match: {colorSimilarityPercent:F1}%");

                                                HandleMatchFound(template.Name, screenshot, maxLoc, template.Width, template.Height, maxVal);
                                                lastMatchTime = DateTime.Now;
                                                matchFound = true;
                                                break;
                                            }
                                        }
                                    }

                                    if (iterationCount % 200 == 0 && !matchFound)
                                    {
                                        //Console.WriteLine($"[SPAWN] Scanning...");
                                    }
                                }
                            }
                        }

                        // Add a small delay between full scans to reduce CPU usage
                        Thread.Sleep(200);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SPAWN] Scan error: {ex.Message}");
                        Thread.Sleep(500);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SPAWN] Watcher thread error: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("[SPAWN] Optimized watcher thread exited");
            }
        }

        private static (bool, double) IsColorSimilar(Mat template, Mat matchedRegion, double allowedDifferencePercent = 20.0)
        {
            // Calculate the mean color of each image
            MCvScalar templateMean = CvInvoke.Mean(template);
            MCvScalar matchedMean = CvInvoke.Mean(matchedRegion);

            // Calculate the Euclidean color distance between the two mean colors
            double colorDistance = Math.Sqrt(
                Math.Pow(templateMean.V0 - matchedMean.V0, 2) +
                Math.Pow(templateMean.V1 - matchedMean.V1, 2) +
                Math.Pow(templateMean.V2 - matchedMean.V2, 2)
            );

            // Maximum possible distance between two colors (black and white)
            double maxPossibleDistance = Math.Sqrt(3 * Math.Pow(255, 2)); // ≈ 441.67

            // Calculate how much the colors differ in percentage
            double differencePercent = 100.0 * (colorDistance / maxPossibleDistance);

            // Calculate similarity in percentage
            double similarityPercent = 100.0 - differencePercent;

            // Check if the difference is within the allowed percent
            bool isSimilar = differencePercent <= allowedDifferencePercent;

            // Optionally log
            //Console.WriteLine($"Color Distance: {colorDistance:F2}");
            //Console.WriteLine($"Difference Percent: {differencePercent:F2}%");
            //Console.WriteLine($"Similarity Percent: {similarityPercent:F2}%");
            //Console.WriteLine($"Allowed Difference: {allowedDifferencePercent}%, Is Similar: {isSimilar}");

            return (isSimilar, similarityPercent);
        }




        // This method handles what happens when a template match is found
        private static void HandleMatchFound(string templateName, Mat screenshot, Point matchLocation, int templateWidth, int templateHeight, double similarity)
        {
            //Console.WriteLine($"[SPAWN] !!! MATCH FOUND !!! - {templateName} - Similarity: {similarity:F3}");

            // Create directories for saving matches if they don't exist
            string matchesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "matches");
            if (!Directory.Exists(matchesDir))
            {
                Directory.CreateDirectory(matchesDir);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string matchFileName = $"match_{templateName}_{timestamp}.png";
            string matchPath = Path.Combine(matchesDir, matchFileName);

            try
            {
                // Capture full window for context
                IntPtr gameWindow = Program.targetWindow;
                GetClientRect(gameWindow, out RECT clientRect);
                int windowWidth = clientRect.Right - clientRect.Left;
                int windowHeight = clientRect.Bottom - clientRect.Top;

                // Capture the full window
                using (Mat fullScreenshot = CaptureWindowAsMat(gameWindow, 0, 0, windowWidth, windowHeight))
                {
                    if (fullScreenshot != null)
                    {
                        // Adjust match location coordinates to full window coordinates
                        int scanLeft = Math.Max(0, scanCenterX - (scanWidth / 2));
                        int scanTop = Math.Max(0, scanCenterY - (scanHeight / 2));

                        Point fullScreenMatchLocation = new Point(
                            scanLeft + matchLocation.X,
                            scanTop + matchLocation.Y
                        );

                        // Draw a rectangle on the full screenshot to highlight the match
                        Rectangle matchRect = new Rectangle(
                            fullScreenMatchLocation.X,
                            fullScreenMatchLocation.Y,
                            templateWidth,
                            templateHeight
                        );

                        CvInvoke.Rectangle(fullScreenshot, matchRect, new MCvScalar(0, 0, 255), 2);

                        // Add text with match information
                        string label = $"{templateName} ({similarity:F2})";
                        Point textLocation = new Point(matchRect.X, Math.Max(0, matchRect.Y - 10));
                        CvInvoke.PutText(fullScreenshot, label, textLocation, FontFace.HersheyComplex, 0.5, new MCvScalar(0, 0, 255), 2);

                        // Save the highlighted match
                        CvInvoke.Imwrite(matchPath, fullScreenshot);
                        //Console.WriteLine($"[SPAWN] Full window match screenshot saved to: {matchPath}");
                    }
                    else
                    {
                        // Fall back to the scan area screenshot
                        CvInvoke.Imwrite(matchPath, screenshot);
                        //Console.WriteLine($"[SPAWN] Scan area match screenshot saved to: {matchPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SPAWN] Error saving match screenshot: {ex.Message}");
            }

            // Trigger the alarm
            TriggerAlarm(templateName);
        }

        private static Mat referenceImage = null;
        private static DateTime referenceImageTime = DateTime.MinValue;
        private static bool isFirstCapture = true;
        private static readonly TimeSpan referenceUpdateInterval = TimeSpan.FromMinutes(10); // Update reference every 10 minutes

        // Color change detection settings
        private static double colorDifferenceThreshold = 30.0; // Threshold for considering a pixel "changed"
        private static double changedPixelPercentageThreshold = 2.0; // Percentage of changed pixels to trigger detection
        private static bool colorChangeAlarmEnabled = true;
        private static readonly object colorDetectionLock = new object();

        private static void TriggerAlarm(string templateName)
        {
            try
            {
                //Console.WriteLine("\n[!!!] CRITICAL ALARM - SPAWN DETECTED [!!!]");
                //Console.WriteLine($"[!!!] Matched template: {templateName}");
                //Console.WriteLine("[!!!] STOPPING ALL THREADS AND SOUNDING ALARM [!!!]\n");

                IntPtr copyWindow = Program.targetWindow;
                // Stop all program threads
                Program.threadFlags["recording"] = false;
                Program.threadFlags["playing"] = false;
                Program.threadFlags["autopot"] = false;
                Program.threadFlags["spawnwatch"] = false;
                Program.memoryReadActive = false;
                Program.programRunning = false;
                Program.StopPositionAlertSound();  // Stop any existing alert sounds

                // Create a thread for the alarm sound - will run asynchronously
                Thread alarmSoundThread = new Thread(() =>
                {
                    try
                    {
                        // Sound the alarm until program exit
                        while (true)
                        {
                            Beep(2000, 200);
                            Thread.Sleep(50);
                            Beep(1500, 200);
                            Thread.Sleep(50);
                            Beep(2000, 200);
                            Thread.Sleep(50);
                            Beep(1500, 200);
                            Thread.Sleep(200);

                            //Console.WriteLine($"[ALARM] CRITICAL: Spawn '{templateName}' detected!");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ALARM] Error in alarm sound: {ex.Message}");
                    }
                });

                // Make alarm thread independent so it can run parallel to panic keys
                alarmSoundThread.IsBackground = true;
                alarmSoundThread.Start();

                // Create a thread for sending panic keys
                Thread panicKeysThread = new Thread(() =>
                {
                    try
                    {
                        // Send key inputs to the game
                        SendPanicKeysToGame(copyWindow);

                        //Console.WriteLine("[ALARM] CRITICAL: Terminating program immediately!");

                        // Force immediate program exit with error code
                        Environment.Exit(1);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ALARM] Error in panic keys: {ex.Message}");
                        Environment.Exit(1); // Still exit even if there's an error
                    }
                });

                // Make sure this thread will continue even if the main thread ends
                panicKeysThread.IsBackground = false;
                panicKeysThread.Start();

                // Wait for panic keys thread to complete
                panicKeysThread.Join();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ALARM] Error triggering alarm: {ex.Message}");
                // Still try to exit the program even if there's an exception
                Environment.Exit(1);
            }
        }

        private static void FocusGameWindow(IntPtr gameWindow)
        {
            if (gameWindow == IntPtr.Zero)
            {
                //Console.WriteLine("[ALARM] Cannot focus - game window handle is invalid");
                return;
            }

            try
            {
                //Console.WriteLine("[ALARM] Attempting to focus game window...");

                // Try to bring the window to the foreground
                if (!SetForegroundWindow(gameWindow))
                {
                    int error = Marshal.GetLastWin32Error();
                    //Console.WriteLine($"[ALARM] Failed to set foreground window. Error code: {error}");

                    // Alternative approach - activate the window first
                    ShowWindow(gameWindow, SW_RESTORE);
                    Thread.Sleep(100);
                    SetForegroundWindow(gameWindow);
                    Thread.Sleep(100);
                }

                // Verify the window is now active
                IntPtr activeWindow = GetForegroundWindow();
                if (activeWindow != gameWindow)
                {
                    //Console.WriteLine("[ALARM] Warning: Game window is not in foreground");
                }
                else
                {
                    //Console.WriteLine("[ALARM] Game window successfully focused");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ALARM] Error focusing window: {ex.Message}");
            }
        }

        private static void SendPanicKeysToGame(IntPtr gameWindow)
        {
            if (gameWindow == IntPtr.Zero)
            {
                //Console.WriteLine("[ALARM] Cannot send keys - game window handle is invalid");
                return;
            }

            try
            {
                // First focus the window
                FocusGameWindow(gameWindow);
                Thread.Sleep(200); // Give the window time to get focus

                //Console.WriteLine("[ALARM] Sending panic keys to game...");

                //Console.WriteLine("[ALARM] Sending ESC 3 times...");
                for (int i = 0; i < 3; i++)
                {
                    SendKeys.SendWait("{ESC}");
                    Thread.Sleep(100);
                }

                Random random = new Random();
                int questionMarkCount = random.Next(4, 8); // 4 to 7 inclusive

                //Console.WriteLine($"[ALARM] Sending {questionMarkCount} question marks...");

                for (int i = 0; i < questionMarkCount; i++)
                {
                    // Send question mark - in SendKeys, shift+/ is represented as "?"
                    SendKeys.SendWait("?");
                    Thread.Sleep(50);
                }

                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(50);

                for (int i = 0; i < questionMarkCount; i++)
                {
                    // Send question mark - in SendKeys, shift+/ is represented as "?"
                    SendKeys.SendWait("?");
                    Thread.Sleep(50);
                }

                // Send ENTER key again
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(50);

                // Prepare for arrow sequence with CTRL
                //Console.WriteLine("[ALARM] Starting CTRL+Arrow key sequence...");
                DateTime endTime = DateTime.Now.AddSeconds(15); // Do this for 15 seconds

                // Improved panic movement code with randomization
                while (DateTime.Now < endTime)
                {
                    // Pick a random direction
                    string[] directions = new string[] { "{UP}", "{RIGHT}", "{DOWN}", "{LEFT}" };
                    Random rand = new Random();

                    // Random number of movements per cycle (100-250 movements)
                    int movementsInCycle = rand.Next(100, 250);

                    for (int i = 0; i < movementsInCycle; i++)
                    {
                        // Select a random direction
                        string direction = directions[rand.Next(directions.Length)];

                        // Randomly decide if we'll use CTRL (70% chance)
                        bool useCtrl = true;

                        // Send the key combo
                        if (useCtrl)
                        {
                            SendKeys.SendWait("^" + direction);  // ^ represents CTRL
                        }
                        else
                        {
                            SendKeys.SendWait(direction);  // Sometimes just send arrow key without CTRL
                        }

                        // Random delay between key presses (20-30 ms)
                        int keyDelay = rand.Next(20, 30);
                        Thread.Sleep(keyDelay);
                    }
                }

                //Console.WriteLine("[ALARM] Finished sending panic keys to game");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ALARM] Error sending panic keys: {ex.Message}");
            }
        }

        // Win32 API declarations
        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
            IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr hdc);

        [DllImport("kernel32.dll")]
        static extern bool Beep(int frequency, int duration);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // ShowWindow command constants
        const int SW_RESTORE = 9;

        // BitBlt constant
        const uint SRCCOPY = 0x00CC0020;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }
    }

    // Add these to your class-level declarations
    static System.Windows.Forms.Form overlayForm = null;
    static List<(int x, int y, int size, DateTime time, string label)> activeHighlights = new List<(int x, int y, int size, DateTime time, string label)>();
    static System.Windows.Forms.Timer cleanupTimer = null;
    static object highlightLock = new object();
    static bool showDebugCenter = true;

    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x00000020;
    const int WS_EX_LAYERED = 0x00080000;

    // Add these Win32 API imports
    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // This method initializes the overlay if it doesn't exist, positioned over the game window
    // Add these variables with your class-level variables at the top of your Program class
    // ===== STATS OVERLAY CONFIGURATION =====
    static int statsOverlayRightOffset = 380;  // Distance from right edge (smaller = closer to right edge)
    static int statsOverlayBottomOffset = 40;  // Distance from bottom edge (smaller = closer to bottom)
    static float statsOverlaySizeScale = 0.92f; // Size multiplier (1.0 = 100%, 1.5 = 150%, 0.8 = 80%)
    static float statsOverlayOpacity = 0.99f;  // Transparency (1.0 = fully opaque, 0.5 = 50% transparent)
                                               // =======================================

    // Updated EnsureOverlayExists method with all configurable parameters
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int WS_EX_TOOLWINDOW = 0x00000080;


    const int HWND_TOPMOST = -1;
    const int SWP_NOMOVE = 0x0002;
    const int SWP_NOSIZE = 0x0001;
    const int SWP_NOACTIVATE = 0x0010;

    // Add this Win32 API declaration with your other DllImport statements
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    static void EnsureOverlayExists()
    {
        // If overlay is already created and valid, just return
        if (overlayForm != null && overlayForm.IsHandleCreated && !overlayForm.IsDisposed)
            return;

        // Reset overlay reference
        overlayForm = null;

        // Create new overlay thread
        Thread overlayThread = new Thread(() =>
        {
            try
            {
                System.Windows.Forms.Form threadForm = null;

                try
                {
                    threadForm = new System.Windows.Forms.Form();

                    // Configure form properties
                    threadForm.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
                    threadForm.ShowInTaskbar = false;
                    threadForm.TopMost = true;
                    threadForm.Opacity = statsOverlayOpacity; // Use configurable opacity
                    threadForm.TransparencyKey = Color.Black;
                    threadForm.BackColor = Color.Black;

                    threadForm.Deactivate += (s, e) => {
                        if (threadForm != null && !threadForm.IsDisposed)
                        {
                            // Re-assert TopMost when the form loses focus
                            threadForm.TopMost = false; // Toggle to refresh the topmost status
                            threadForm.TopMost = true;
                        }
                    };

                    int baseWidth = 300;
                    int baseHeight = 200;
                    threadForm.Width = (int)(baseWidth * statsOverlaySizeScale);
                    threadForm.Height = (int)(baseHeight * statsOverlaySizeScale);
                    threadForm.StartPosition = System.Windows.Forms.FormStartPosition.Manual;

                    try
                    {
                        GetWindowRect(targetWindow, out RECT gameWindowRect);
                        // Position using configurable offsets
                        threadForm.Location = new Point(
                            Math.Max(0, gameWindowRect.Right - threadForm.Width - statsOverlayRightOffset),
                            gameWindowRect.Bottom - threadForm.Height - statsOverlayBottomOffset);
                    }
                    catch
                    {
                        threadForm.Location = new Point(
                            Math.Max(0, Screen.PrimaryScreen.WorkingArea.Right - threadForm.Width - statsOverlayRightOffset),
                            Screen.PrimaryScreen.WorkingArea.Bottom - threadForm.Height - statsOverlayBottomOffset);
                    }

                    int exStyle = GetWindowLong(threadForm.Handle, GWL_EXSTYLE);
                    exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                    SetWindowLong(threadForm.Handle, GWL_EXSTYLE, exStyle);

                    // Set up painting
                    threadForm.Paint += (sender, e) =>
                    {
                        try
                        {
                            // Apply scaling to graphics for text rendering
                            if (statsOverlaySizeScale != 1.0f)
                            {
                                e.Graphics.ScaleTransform(statsOverlaySizeScale, statsOverlaySizeScale);
                            }

                            // Fill background with semi-transparent dark color (no border)
                            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                            {
                                // When using scaling, adjust the rectangle to fill the scaled area
                                float scaledWidth = threadForm.Width / statsOverlaySizeScale;
                                float scaledHeight = threadForm.Height / statsOverlaySizeScale;
                                e.Graphics.FillRectangle(bgBrush, 0, 0, scaledWidth, scaledHeight);
                            }

                            // Calculate font sizes based on scaling
                            float titleFontSize = 10.0f;
                            float statsFontSize = 9.0f;
                            float smallFontSize = 8.0f;

                            // Increased font sizes
                            using (Font titleFont = new Font("Arial", titleFontSize, FontStyle.Bold))
                            using (Font statsFont = new Font("Arial", statsFontSize, FontStyle.Regular))
                            using (Font smallFont = new Font("Arial", smallFontSize, FontStyle.Regular))
                            using (Brush whiteBrush = new SolidBrush(Color.White))
                            using (Brush greenBrush = new SolidBrush(Color.LightGreen))
                            using (Brush blueBrush = new SolidBrush(Color.LightBlue))  // Blue for mana
                            using (Brush yellowBrush = new SolidBrush(Color.Yellow))
                            using (Brush orangeBrush = new SolidBrush(Color.Orange))
                            using (Brush pinkBrush = new SolidBrush(Color.LightPink))  // For invisibility
                            using (Brush redBrush = new SolidBrush(Color.LightCoral))  // For target info
                            {
                                // Calculate scaled dimensions
                                float scaledWidth = threadForm.Width / statsOverlaySizeScale;

                                int y = 5;  // Start position
                                int rightPadding = 20;  // Padding from right edge
                                int lineSpacing = 18;   // Increased spacing between lines (was 14)

                                // Get current stats
                                double hpPercent, manaPercent;
                                int currentX, currentY, currentZ, currentTargetId, currentInvisibilityCode, currentOutfitValue;
                                string currentTargetName = "";
                                lock (memoryLock)
                                {
                                    hpPercent = (curHP / maxHP) * 100;
                                    manaPercent = (curMana / maxMana) * 100;
                                    currentX = posX;
                                    currentY = posY;
                                    currentZ = posZ;
                                    currentTargetId = targetId;
                                    currentInvisibilityCode = invisibilityCode;
                                    currentOutfitValue = currentOutfit;
                                }

                                // Get target monster info if we have a target
                                int monsterX = 0, monsterY = 0, monsterZ = 0;
                                if (currentTargetId != 0)
                                {
                                    var (mX, mY, mZ, mName) = GetTargetMonsterInfo();
                                    monsterX = mX;
                                    monsterY = mY;
                                    monsterZ = mZ;
                                    currentTargetName = mName;
                                }

                                // Title
                                string titleText = "RealeraDX - Live Stats";
                                SizeF titleSize = e.Graphics.MeasureString(titleText, titleFont);
                                e.Graphics.DrawString(titleText, titleFont, whiteBrush,
                                    scaledWidth - titleSize.Width - rightPadding, y);
                                y += lineSpacing + 2; // Extra spacing after title

                                // Format right-aligned stats 
                                DrawRightAlignedStat(e, "HP:", $"{curHP:F0}/{maxHP:F0} ({hpPercent:F1}%)",
                                    statsFont, hpPercent < 50 ? orangeBrush : greenBrush,
                                    scaledWidth - rightPadding, ref y, lineSpacing);

                                DrawRightAlignedStat(e, "Mana:", $"{curMana:F0}/{maxMana:F0} ({manaPercent:F1}%)",
                                    statsFont, blueBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                // Position coordinates - each on separate line
                                DrawRightAlignedStat(e, "X:", $"{currentX}", statsFont, whiteBrush,
                                    scaledWidth - rightPadding, ref y, lineSpacing);

                                DrawRightAlignedStat(e, "Y:", $"{currentY}", statsFont, whiteBrush,
                                    scaledWidth - rightPadding, ref y, lineSpacing);

                                DrawRightAlignedStat(e, "Z:", $"{currentZ}", statsFont, whiteBrush,
                                    scaledWidth - rightPadding, ref y, lineSpacing);

                                // Add invisibility code
                                DrawRightAlignedStat(e, "Invisibility:", $"{currentInvisibilityCode} " +
                                    (currentInvisibilityCode == 2 ? "(Ring ON)" : "(Ring OFF)"),
                                    statsFont, pinkBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                // Add outfit value
                                DrawRightAlignedStat(e, "Outfit:", $"{currentOutfitValue}",
                                    statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                y += 5; // Extra spacing before target section

                                // Target information section
                                if (currentTargetId != 0)
                                {
                                    string targetHeader = "Target Info:";
                                    SizeF headerSize = e.Graphics.MeasureString(targetHeader, statsFont);
                                    e.Graphics.DrawString(targetHeader, statsFont, redBrush,
                                        scaledWidth - headerSize.Width - rightPadding, y);
                                    y += lineSpacing;

                                    // Target ID and name
                                    DrawRightAlignedStat(e, "ID:", $"{currentTargetId}",
                                        statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                    if (!string.IsNullOrEmpty(currentTargetName))
                                    {
                                        DrawRightAlignedStat(e, "Name:", $"{currentTargetName}",
                                            statsFont, redBrush, scaledWidth - rightPadding, ref y, lineSpacing);
                                    }

                                    // Target coordinates
                                    if (monsterX != 0 || monsterY != 0 || monsterZ != 0)
                                    {
                                        DrawRightAlignedStat(e, "Pos:", $"({monsterX}, {monsterY}, {monsterZ})",
                                            statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                        // Calculate distance to target
                                        int distanceX = Math.Abs(monsterX - currentX);
                                        int distanceY = Math.Abs(monsterY - currentY);
                                        int totalDistance = distanceX + distanceY;

                                        DrawRightAlignedStat(e, "Distance:", $"{totalDistance} steps",
                                            statsFont, totalDistance > 5 ? orangeBrush : greenBrush,
                                            scaledWidth - rightPadding, ref y, lineSpacing);
                                    }
                                }
                                else
                                {
                                    DrawRightAlignedStat(e, "Target:", "None",
                                        statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);
                                }

                                y += 5; // Extra spacing before features section

                                // Features heading in yellow
                                string featuresText = "Active Features:";
                                SizeF featuresSize = e.Graphics.MeasureString(featuresText, statsFont);
                                e.Graphics.DrawString(featuresText, statsFont, yellowBrush,
                                    scaledWidth - featuresSize.Width - rightPadding, y);
                                y += lineSpacing;

                                // Format features
                                DrawRightAlignedFeature(e, "Auto-Potions:", threadFlags["autopot"],
                                    statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                DrawRightAlignedFeature(e, "Recording:", threadFlags["recording"],
                                    statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                DrawRightAlignedFeature(e, "Playback:", threadFlags["playing"],
                                    statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                DrawRightAlignedFeature(e, "Click Overlay:", clickOverlayActive,
                                    statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                // If we're playing a path, show progress
                                if (threadFlags["playing"] && currentTarget != null)
                                {
                                    y += 2; // Small extra gap

                                    // Target distance
                                    int distanceX = Math.Abs(currentTarget.X - currentX);
                                    int distanceY = Math.Abs(currentTarget.Y - currentY);
                                    DrawRightAlignedStat(e, "Waypoint:", $"{distanceX + distanceY} steps",
                                        statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                    float progress = (float)(currentCoordIndex + 1) / totalCoords;

                                    // Progress text
                                    string progressText = $"Path: {(progress * 100):F1}% ({currentCoordIndex + 1}/{totalCoords})";
                                    SizeF progressSize = e.Graphics.MeasureString(progressText, statsFont);
                                    e.Graphics.DrawString(progressText, statsFont, whiteBrush,
                                        scaledWidth - progressSize.Width - rightPadding, y);
                                    y += lineSpacing;

                                    // Progress bar
                                    int progressWidth = (int)(scaledWidth - 40 - rightPadding); // with margins
                                    int barHeight = 6; // Slightly taller bar
                                    int barX = 20; // Left margin
                                    int filledWidth = (int)(progressWidth * progress);

                                    e.Graphics.FillRectangle(new SolidBrush(Color.DarkGray), barX, y, progressWidth, barHeight);
                                    e.Graphics.FillRectangle(new SolidBrush(Color.LimeGreen), barX, y, filledWidth, barHeight);

                                    // Add minimal spacing after bar
                                    y += barHeight + 2;
                                }

                                // Display offset and scaling values for easy adjustment
                                scaledWidth = threadForm.Width / statsOverlaySizeScale;
                                float scaledHeight = threadForm.Height / statsOverlaySizeScale;

                                //string configText = $"R:{statsOverlayRightOffset} B:{statsOverlayBottomOffset} S:{statsOverlaySizeScale:F1}";
                                //using (Font configFont = new Font("Arial", 7.0f, FontStyle.Regular))
                                //{
                                //    SizeF configSize = e.Graphics.MeasureString(configText, configFont);
                                //    e.Graphics.DrawString(configText, configFont, whiteBrush,
                                //        5, scaledHeight - configSize.Height - 5);
                                //}

                                //// Version and time info at bottom right
                                //string versionText = $"v1.2.0 | {DateTime.Now.ToString("HH:mm:ss")}";
                                //using (Font versionFont = new Font("Arial", 7.0f, FontStyle.Regular))
                                //{
                                //    SizeF versionSize = e.Graphics.MeasureString(versionText, versionFont);
                                //    e.Graphics.DrawString(versionText, versionFont, whiteBrush,
                                //        scaledWidth - versionSize.Width - rightPadding,
                                //        scaledHeight - versionSize.Height - 5);
                                //}
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[OVERLAY] Paint error: {ex.Message}");
                        }
                    };

                    // Set up timer for cleanup and window position update
                    System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer { Interval = 100 };

                    updateTimer.Tick += (sender, e) =>
                    {
                        try
                        {
                            // Update overlay position to track the game window
                            GetWindowRect(targetWindow, out RECT currentGameRect);

                            // Only calculate new size if game window is valid
                            if (currentGameRect.Right > currentGameRect.Left &&
                                currentGameRect.Bottom > currentGameRect.Top)
                            {
                                // Apply size scaling
                                int baseWidth = (currentGameRect.Right - currentGameRect.Left) / 3;
                                int newWidth = (int)(baseWidth * statsOverlaySizeScale);

                                int contentLines = 14;
                                if (threadFlags["playing"] && currentTarget != null)
                                    contentLines += 3;
                                if (targetId != 0)
                                    contentLines += 4;

                                int lineHeight = 18;
                                int bottomMargin = 20;
                                int baseHeight = contentLines * lineHeight + bottomMargin;
                                int newHeight = (int)(baseHeight * statsOverlaySizeScale);

                                int newX = Math.Max(0, currentGameRect.Right - newWidth - statsOverlayRightOffset);
                                int newY = Math.Max(0, currentGameRect.Bottom - newHeight - statsOverlayBottomOffset);

                                // Only update if dimensions actually changed
                                if (newX != threadForm.Left || newY != threadForm.Top ||
                                    newWidth != threadForm.Width || newHeight != threadForm.Height)
                                {
                                    threadForm.Location = new Point(newX, newY);
                                    threadForm.Size = new Size(newWidth, newHeight);
                                }
                            }

                            if (threadForm.IsHandleCreated)
                            {
                                // Force window to stay topmost and click-through
                                int currentExStyle = GetWindowLong(threadForm.Handle, GWL_EXSTYLE);
                                if ((currentExStyle & (WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW))
                                    != (WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW))
                                {
                                    // Re-apply all the required window styles
                                    SetWindowLong(threadForm.Handle, GWL_EXSTYLE,
                                        currentExStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
                                }
                                SetWindowPos(threadForm.Handle, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                            }

                            // Always refresh display
                            threadForm.Invalidate();

                            // IMPORTANT: Keep form on top regardless of focus state
                            if (!threadForm.TopMost)
                            {
                                threadForm.TopMost = true;
                            }

                            // If main program has stopped, stop timer and close form
                            // Only check programRunning and memoryReadActive, not focus-related states
                            if (!programRunning || !memoryReadActive || !threadFlags["overlay"] ||
                                overlayForm != threadForm)
                            {
                                updateTimer.Stop();
                                threadForm.Close();
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[OVERLAY] Update error: {ex.Message}");
                        }
                    };

                    // Make the new form available for other threads
                    overlayForm = threadForm;

                    // Start the timer
                    updateTimer.Start();

                    // Show the form
                    threadForm.Show();

                    // Message loop using DoEvents
                    DateTime lastCheckTime = DateTime.MinValue;
                    while (!threadForm.IsDisposed && programRunning && memoryReadActive &&
                           threadFlags["overlay"] && overlayForm == threadForm)
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(10);

                        // Periodically check if we should exit and refresh TopMost status
                        DateTime now = DateTime.Now;
                        if ((now - lastCheckTime).TotalSeconds >= 1)
                        {
                            lastCheckTime = now;

                            // Ensure the form maintains TopMost
                            if (!threadForm.TopMost)
                            {
                                threadForm.TopMost = true;
                            }

                            // Check exit conditions
                            if (!programRunning || !memoryReadActive || !threadFlags["overlay"] ||
                                overlayForm != threadForm)
                            {
                                break;
                            }
                        }
                    }

                    // When we exit the loop, stop the timer
                    updateTimer.Stop();
                    updateTimer.Dispose();

                    // Clear reference if this is still the active form
                    if (overlayForm == threadForm)
                    {
                        overlayForm = null;
                    }

                    // Close the form if it's not already disposed
                    if (!threadForm.IsDisposed)
                    {
                        threadForm.Close();
                        threadForm.Dispose();
                    }
                }
                finally
                {
                    // Ensure resources are cleaned up even on exception
                    if (threadForm != null && !threadForm.IsDisposed)
                    {
                        try
                        {
                            threadForm.Close();
                            threadForm.Dispose();
                        }
                        catch { }
                    }

                    // Final check to clear overlay reference
                    if (overlayForm == threadForm)
                    {
                        overlayForm = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OVERLAY] Overlay thread error: {ex.Message}");
                overlayForm = null;
            }
        });

        // Configure and start the thread
        overlayThread.IsBackground = true;
        overlayThread.SetApartmentState(ApartmentState.STA);
        overlayThread.Start();

        // Give the overlay a moment to initialize
        Sleep(100);
    }


    // Add a helper method to adjust overlay configuration during runtime
    static void AdjustOverlayConfig(int rightOffsetChange, int bottomOffsetChange, float scaleChange)
    {
        statsOverlayRightOffset = Math.Max(10, statsOverlayRightOffset + rightOffsetChange);
        statsOverlayBottomOffset = Math.Max(10, statsOverlayBottomOffset + bottomOffsetChange);
        statsOverlaySizeScale = Math.Max(0.5f, Math.Min(2.0f, statsOverlaySizeScale + scaleChange));

        //Console.WriteLine($"[OVERLAY] Config: Right Offset={statsOverlayRightOffset}, " +
        ////$"Bottom Offset={statsOverlayBottomOffset}, " +
        ////$"Scale={statsOverlaySizeScale:F1}");

        // Restart overlay to apply changes
        if (overlayForm != null)
        {
            StopOverlay();
            Sleep(200);
            StartOverlay();
        }
    }

    // Helper method to draw a right-aligned stat line
    // Updated helper methods with line spacing parameter
    static void DrawRightAlignedStat(PaintEventArgs e, string label, string value, Font font,
       Brush brush, float rightEdge, ref int y, int lineSpacing)
    {
        string fullText = $"{label} {value}";
        SizeF textSize = e.Graphics.MeasureString(fullText, font);
        e.Graphics.DrawString(fullText, font, brush, rightEdge - textSize.Width, y);
        y += lineSpacing; // Use the provided line spacing
    }

    // Updated DrawRightAlignedFeature to also accept float
    static void DrawRightAlignedFeature(PaintEventArgs e, string label, bool enabled, Font font,
        Brush brush, float rightEdge, ref int y, int lineSpacing)
    {
        string status = enabled ? "✅" : "❌";
        string fullText = $"{label} {status}";
        SizeF textSize = e.Graphics.MeasureString(fullText, font);
        e.Graphics.DrawString(fullText, font, brush, rightEdge - textSize.Width, y);
        y += lineSpacing; // Use the provided line spacing
    }

    class ClickHighlight
    {
        public int GameX { get; set; }        // X coordinate in game client space
        public int GameY { get; set; }        // Y coordinate in game client space
        public int Size { get; set; }         // Size of highlight square
        public DateTime ExpireTime { get; set; } // When this highlight should disappear
        public string Label { get; set; }     // Optional label to display

        // Calculate how much time is remaining before expiration (0-1)
        public float GetRemainingTimePercentage()
        {
            double msRemaining = (ExpireTime - DateTime.Now).TotalMilliseconds;
            return Math.Max(0f, Math.Min(1f, (float)(msRemaining / 1000.0)));
        }

        // Is this highlight still valid?
        public bool IsExpired()
        {
            return DateTime.Now >= ExpireTime;
        }
    }

    // Add this to manage overlay start/stop cleanly
    static void StartOverlay()
    {
        if (overlayForm != null && !overlayForm.IsDisposed)
            return;

        //Console.WriteLine("[OVERLAY] Starting overlay display");
        EnsureOverlayExists();
    }

    static void StopOverlay()
    {
        if (overlayForm == null || overlayForm.IsDisposed)
            return;

        //Console.WriteLine("[OVERLAY] Stopping overlay display");
        var form = overlayForm;
        overlayForm = null; // Clear reference first to prevent further access

        try
        {
            // Create a dedicated thread to close the form to avoid deadlocks
            Thread closeThread = new Thread(() =>
            {
                try
                {
                    if (!form.IsDisposed)
                    {
                        form.Close();
                        form.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OVERLAY] Error in close thread: {ex.Message}");
                }
            });

            closeThread.IsBackground = true;
            closeThread.SetApartmentState(ApartmentState.STA);
            closeThread.Start();

            // We don't wait for the thread to complete to avoid blocking
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OVERLAY] Error closing overlay: {ex.Message}");
        }
    }

    // Make sure you have this function to get the window rectangle
    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // Add this constant
    const int NULL_PEN = 8;

    [DllImport("gdi32.dll")]
    static extern bool Rectangle(IntPtr hdc, int left, int top, int right, int bottom);

    static extern bool Ellipse(IntPtr hdc, int left, int top, int right, int bottom);
    [DllImport("user32.dll")]
    static extern bool FillRect(IntPtr hDC, [In] ref RECT lprc, IntPtr hbr);
    [DllImport("user32.dll")]
    static extern IntPtr CreateIconIndirect([In] ref ICONINFO piconinfo);
    [StructLayout(LayoutKind.Sequential)]
    struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }
    const int WHITE_BRUSH = 0;

    // Add these required imports/constants
    [DllImport("gdi32.dll")]
    static extern int SetROP2(IntPtr hdc, int fnDrawMode);

    const int R2_XORPEN = 7;
    const int PS_SOLID = 0;

    [DllImport("gdi32.dll")]
    static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);

    [DllImport("gdi32.dll")]
    static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr lpPoint);

    [DllImport("gdi32.dll")]
    static extern bool LineTo(IntPtr hdc, int x, int y);

    // Add these required Win32 API declarations
    [DllImport("gdi32.dll")]
    static extern IntPtr CreateSolidBrush(uint colorRef);

    [DllImport("gdi32.dll")]
    static extern uint RGB(int r, int g, int b);

    [DllImport("gdi32.dll")]
    static extern IntPtr GetStockObject(int fnObject);

    [DllImport("user32.dll")]
    static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    // Constants
    const int HOLLOW_BRUSH = 5;

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    // Replace or add this declaration
    [DllImport("gdi32.dll", EntryPoint = "RGB")]
    static extern uint RGB(byte r, byte g, byte b);

    // Add this helper method
    static uint MakeRGB(byte r, byte g, byte b)
    {
        return ((uint)r) | (((uint)g) << 8) | (((uint)b) << 16);
    }

    static object clickOverlayLock = new object();
    static List<(int x, int y, Color color, DateTime created, DateTime expires)> clickPositions =
    new List<(int x, int y, Color color, DateTime created, DateTime expires)>();
    static Thread clickOverlayThread = null;
    static bool clickOverlayActive = false;
    static Form clickOverlayForm = null;
    static readonly Color DEFAULT_LEFT_CLICK_COLOR = Color.FromArgb(180, 0, 255, 0);     // Bright green with more alpha
    static readonly Color DEFAULT_RIGHT_CLICK_COLOR = Color.FromArgb(180, 255, 128, 0);  // Bright orange with more alpha
    static readonly Color WAYPOINT_CLICK_COLOR = Color.FromArgb(180, 255, 0, 255);

    // Add this method to start the click overlay
    static void StartClickOverlay()
    {
        lock (clickOverlayLock)
        {
            if (clickOverlayActive)
            {
                //Console.WriteLine("[CLICK OVERLAY] Already running");
                return;
            }

            // Clear any existing click positions if restarting
            clickPositions.Clear();
            clickOverlayActive = true;

            // Start the overlay thread
            clickOverlayThread = new Thread(() =>
            {
                try
                {
                    //Console.WriteLine("[CLICK OVERLAY] Thread started");
                    RunClickOverlay();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CLICK OVERLAY] Thread error: {ex.Message}");
                }
                finally
                {
                    lock (clickOverlayLock)
                    {
                        clickOverlayActive = false;
                        clickOverlayForm = null;
                    }
                    //Console.WriteLine("[CLICK OVERLAY] Thread exited");
                }
            });

            clickOverlayThread.IsBackground = true;
            clickOverlayThread.SetApartmentState(ApartmentState.STA);
            clickOverlayThread.Start();
        }
    }

    // Add this method to stop the click overlay
    static void StopClickOverlay()
    {
        lock (clickOverlayLock)
        {
            if (!clickOverlayActive)
                return;

            clickOverlayActive = false;
            //Console.WriteLine("[CLICK OVERLAY] Stopping...");

            // Close the form if it exists
            Form form = clickOverlayForm;
            if (form != null && !form.IsDisposed)
            {
                try
                {
                    if (form.InvokeRequired)
                    {
                        form.BeginInvoke(new Action(() =>
                        {
                            try { form.Close(); } catch { }
                        }));
                    }
                    else
                    {
                        form.Close();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CLICK OVERLAY] Error closing form: {ex.Message}");
                }
            }
        }
    }




    // 5. Modify the timer logic to remove expired markers
    // Modified RunClickOverlay method for waypoint clicks
    static void RunClickOverlay()
    {
        Form form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            TopMost = true,
            Opacity = 0.8,
            TransparencyKey = Color.Black,
            BackColor = Color.Black
        };

        // Set position
        if (targetWindow != IntPtr.Zero)
        {
            GetWindowRect(targetWindow, out RECT rect);
            form.Location = new Point(rect.Left, rect.Top);
            form.Size = new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        else
        {
            form.Location = new Point(0, 0);
            form.Size = new Size(800, 600);
        }

        // Make form transparent to mouse events
        int exStyle = GetWindowLong(form.Handle, GWL_EXSTYLE);
        SetWindowLong(form.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);

        // Set up painting - optimization: only redraw when something changes
        form.Paint += (sender, e) =>
        {
            try
            {
                // Draw all click positions
                lock (clickOverlayLock)
                {
                    // Only proceed if we have game window and clicks to show
                    if (targetWindow == IntPtr.Zero || clickPositions.Count == 0)
                        return;

                    // Get the game window position relative to screen
                    GetWindowRect(targetWindow, out RECT gameRect);

                    foreach (var click in clickPositions)
                    {
                        // Skip expired clicks (should be rare as timer should clean them)
                        if (DateTime.Now > click.expires)
                            continue;

                        // Calculate click position relative to overlay form
                        int relX = click.x - gameRect.Left;
                        int relY = click.y - gameRect.Top;

                        if (relX >= 0 && relY >= 0 && relX < form.Width && relY < form.Height)
                        {
                            // Determine if this is a waypoint click (magenta color)
                            bool isWaypoint = click.color.R > 200 && click.color.G < 100 && click.color.B > 200;

                            // Smaller size for waypoints
                            int squareSize = isWaypoint ?
                                Math.Max(pixelSize, 20) : // Smaller size for waypoints
                                Math.Max(pixelSize, 24);  // Normal size for regular clicks

                            // Calculate the center of the square
                            int left = relX - (squareSize / 2);
                            int top = relY - (squareSize / 2);

                            // For waypoints, no alpha fade - use full alpha until expiration
                            // For regular clicks, maintain the gradual fade
                            int alpha;
                            if (isWaypoint)
                            {
                                // No alpha fade for waypoints - maintain full opacity until expiration
                                alpha = 180; // Fixed alpha value, no gradual fade
                            }
                            else
                            {
                                // Calculate time remaining as a percentage for regular clicks
                                TimeSpan timeRemaining = click.expires - DateTime.Now;
                                double remainingPercent = timeRemaining.TotalMilliseconds / CLICK_MARKER_LIFESPAN.TotalMilliseconds;
                                alpha = (int)(remainingPercent * 180);
                                alpha = Math.Max(50, Math.Min(255, alpha)); // Clamp between 50 and 255
                            }

                            // Create color with appropriate alpha
                            Color displayColor = Color.FromArgb(
                                alpha,
                                click.color.R,
                                click.color.G,
                                click.color.B
                            );

                            // Draw the marker with appropriate style
                            if (isWaypoint)
                            {
                                // For waypoints: draw a filled circle with crosshairs
                                int diameter = squareSize;
                                int radius = diameter / 2;

                                // Draw filled circle
                                using (SolidBrush brush = new SolidBrush(displayColor))
                                {
                                    e.Graphics.FillEllipse(brush, left, top, diameter, diameter);
                                }

                                // Draw crosshairs
                                using (Pen crosshairPen = new Pen(Color.FromArgb(alpha, 255, 255, 255), 1)) // Thinner crosshairs
                                {
                                    // Horizontal line
                                    e.Graphics.DrawLine(
                                        crosshairPen,
                                        left, top + radius,
                                        left + diameter, top + radius
                                    );

                                    // Vertical line
                                    e.Graphics.DrawLine(
                                        crosshairPen,
                                        left + radius, top,
                                        left + radius, top + diameter
                                    );

                                    // Circle outline
                                    e.Graphics.DrawEllipse(
                                        crosshairPen,
                                        left, top, diameter, diameter
                                    );
                                }
                            }
                            else
                            {
                                // For regular clicks: draw a standard square
                                using (SolidBrush brush = new SolidBrush(displayColor))
                                {
                                    e.Graphics.FillRectangle(brush, left, top, squareSize, squareSize);
                                }

                                // Add a border for better visibility
                                using (Pen borderPen = new Pen(Color.FromArgb(alpha, 255, 255, 255), 1))
                                {
                                    e.Graphics.DrawRectangle(borderPen, left, top, squareSize, squareSize);
                                }
                            }

                            // Only display time for clicks that last longer than 0.3 seconds
                            // This reduces text rendering which is expensive
                            TimeSpan remainingTime = click.expires - DateTime.Now;
                            if (remainingTime.TotalSeconds > 0.3)
                            {
                                // Display remaining time (in tenths of a second)
                                string timeText = $"{remainingTime.TotalSeconds:F1}s";

                                // Draw countdown timer INSIDE the marker
                                using (Font font = new Font("Arial", isWaypoint ? 8 : 8, FontStyle.Bold))
                                {
                                    // Measure text to center it
                                    SizeF textSize = e.Graphics.MeasureString(timeText, font);
                                    float textX = left + (squareSize - textSize.Width) / 2;
                                    float textY = top + (squareSize - textSize.Height) / 2;

                                    // Draw text with improved visibility
                                    using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                                    {
                                        e.Graphics.DrawString(timeText, font, textBrush, textX, textY);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CLICK OVERLAY] Paint error: {ex.Message}");
            }
        };

        // Optimized timer implementation with lower update frequency
        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer
        {
            Interval = 100  // Reduced from 33ms to 100ms (10 updates per second instead of 30)
        };

        DateTime lastCleanupTime = DateTime.MinValue;
        const int CLEANUP_INTERVAL_MS = 500; // Clean up every 500ms instead of every tick

        timer.Tick += (sender, e) =>
        {
            try
            {
                DateTime now = DateTime.Now;
                bool shouldUpdatePosition = false;
                bool shouldRemoveExpired = false;

                // Only update form position when needed (not every frame)
                if ((now - lastCleanupTime).TotalMilliseconds >= CLEANUP_INTERVAL_MS)
                {
                    shouldUpdatePosition = true;
                    shouldRemoveExpired = true;
                    lastCleanupTime = now;
                }

                // Update overlay position to track game window (less frequently)
                if (shouldUpdatePosition && targetWindow != IntPtr.Zero)
                {
                    GetWindowRect(targetWindow, out RECT gameRect);
                    if (gameRect.Right > gameRect.Left && gameRect.Bottom > gameRect.Top)
                    {
                        int newWidth = gameRect.Right - gameRect.Left;
                        int newHeight = gameRect.Bottom - gameRect.Top;

                        if (form.Left != gameRect.Left || form.Top != gameRect.Top ||
                            form.Width != newWidth || form.Height != newHeight)
                        {
                            form.Location = new Point(gameRect.Left, gameRect.Top);
                            form.Size = new Size(newWidth, newHeight);
                        }
                    }
                }

                // Remove expired items from the list (less frequently)
                if (shouldRemoveExpired)
                {
                    lock (clickOverlayLock)
                    {
                        int countBefore = clickPositions.Count;

                        // Remove all expired click positions
                        clickPositions.RemoveAll(click => now >= click.expires);

                        int removed = countBefore - clickPositions.Count;
                        if (removed > 0)
                        {
                            //Console.WriteLine($"[CLICK OVERLAY] Removed {removed} expired click markers. {clickPositions.Count} active");
                        }
                    }
                }

                // Only invalidate if we have active positions to draw
                lock (clickOverlayLock)
                {
                    if (clickPositions.Count > 0)
                    {
                        form.Invalidate();
                    }
                }

                // Check if we should exit
                lock (clickOverlayLock)
                {
                    if (!clickOverlayActive || clickOverlayForm != form)
                    {
                        timer.Stop();
                        form.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CLICK OVERLAY] Timer error: {ex.Message}");
            }
        };

        // Store the form reference
        lock (clickOverlayLock)
        {
            clickOverlayForm = form;
        }

        timer.Start();
        form.Show();

        // Message loop with less CPU usage
        DateTime lastDoEventsTime = DateTime.MinValue;
        const int DO_EVENTS_INTERVAL_MS = 25; // Process events once per 25ms instead of every 10ms

        while (clickOverlayActive && clickOverlayForm == form && !form.IsDisposed)
        {
            DateTime now = DateTime.Now;
            if ((now - lastDoEventsTime).TotalMilliseconds >= DO_EVENTS_INTERVAL_MS)
            {
                Application.DoEvents();
                lastDoEventsTime = now;
            }
            else
            {
                // Just a short sleep to prevent CPU hammering
                Thread.Sleep(5);
            }
        }

        // Clean up
        timer.Stop();
        timer.Dispose();

        if (!form.IsDisposed)
        {
            form.Close();
            form.Dispose();
        }
    }

    // Also update the RecordWaypointClick method to change the expiration time
    static void RecordWaypointClick(int x, int y)
    {
        lock (clickOverlayLock)
        {
            if (!clickOverlayActive)
                return;

            // Get screen coordinates
            POINT screenPoint = new POINT { X = x, Y = y };
            ClientToScreen(targetWindow, ref screenPoint);

            // Calculate creation and expiration times
            DateTime creationTime = DateTime.Now;

            // MODIFIED: Slightly shorter lifetime for waypoint clicks
            DateTime expirationTime = creationTime.Add(TimeSpan.FromSeconds(0.5)); // Reduced from 0.7 seconds

            // Add to the list with the waypoint color
            clickPositions.Add((
                screenPoint.X,
                screenPoint.Y,
                WAYPOINT_CLICK_COLOR,
                creationTime,
                expirationTime
            ));
        }
    }

    // Add this method to record a click position
    static readonly TimeSpan CLICK_MARKER_LIFESPAN = TimeSpan.FromSeconds(0.8);

    // 2. Modify the RecordClickPosition method to include expiration time
    static void RecordClickPosition(int x, int y, bool isLeftClick)
    {
        lock (clickOverlayLock)
        {
            if (!clickOverlayActive)
                return;

            // Get screen coordinates
            POINT screenPoint = new POINT { X = x, Y = y };
            ClientToScreen(targetWindow, ref screenPoint);

            // Calculate expiration time (current time + lifespan)
            DateTime creationTime = DateTime.Now;
            DateTime expirationTime = creationTime.Add(CLICK_MARKER_LIFESPAN);

            // Add to the list with the appropriate color
            Color clickColor = isLeftClick ? DEFAULT_LEFT_CLICK_COLOR : DEFAULT_RIGHT_CLICK_COLOR;

            // Add creation time and expiration time
            clickPositions.Add((
                screenPoint.X,
                screenPoint.Y,
                clickColor,
                creationTime,
                expirationTime  // New expiration time
            ));

            //Console.WriteLine($"[CLICK OVERLAY] Recorded {(isLeftClick ? "left" : "right")} click at ({screenPoint.X}, {screenPoint.Y}) - expires in {CLICK_MARKER_LIFESPAN.TotalMilliseconds}ms");
        }
    }

    static bool lootRecognizerActive = true;
    static Thread lootRecognizerThread = null;
    static readonly object lootRecognizerLock = new object();
    static Dictionary<string, Mat> lootTemplates = new Dictionary<string, Mat>();
    static string dropsDirectoryPath = "drops";
    static readonly TimeSpan LOOT_SCAN_INTERVAL = TimeSpan.FromSeconds(1); // Scan for loot every 2 seconds
    static readonly TimeSpan DRAG_OPERATION_COOLDOWN = TimeSpan.FromSeconds(1); // Minimum time between drag operations
    static DateTime lastDragOperationTime = DateTime.MinValue;
    static double lootMatchThreshold = 0.92; // Similarity threshold for matching loot items

    static void LootRecognizerThread()
    {
        //Console.WriteLine("[LOOT] Loot recognizer thread started");

        try
        {
            // Create drops directory if it doesn't exist
            if (!Directory.Exists(dropsDirectoryPath))
            {
                Directory.CreateDirectory(dropsDirectoryPath);
                //Console.WriteLine($"[LOOT] Created drops directory: {Path.GetFullPath(dropsDirectoryPath)}");
                //Console.WriteLine("[LOOT] Place PNG or JPG images of items in this directory to recognize them");
            }

            // Load all item templates
            LoadLootTemplates();

            if (lootTemplates.Count == 0)
            {
                Console.WriteLine($"[LOOT] No loot item templates found in {dropsDirectoryPath}");
                Console.WriteLine("[LOOT] Add .png or .jpg item images to detect them");
            }
            else
            {
                Console.WriteLine($"[LOOT] Loaded {lootTemplates.Count} item templates to recognize:");
                foreach (var template in lootTemplates.Keys)
                {
                    Console.WriteLine($"[LOOT] - {template}");
                }
            }

            // Check if the platinum template is available
            if (lootTemplates.ContainsKey(PLATINUM_TEMPLATE_NAME))
            {
                // Console.WriteLine($"[LOOT] Platinum template found - will enable right-click after dragging");
            }
            else
            {
                Console.WriteLine($"[LOOT] WARNING: Platinum template '{PLATINUM_TEMPLATE_NAME}' not found");
            }

            // Using fewer time checks to reduce CPU usage
            DateTime lastScanTime = DateTime.MinValue;

            while (memoryReadActive && lootRecognizerActive)
            {
                try
                {
                    DateTime now = DateTime.Now;

                    // Check if we need to scan inventory for platinum
                    if (shouldScanInventoryForPlatinum && now >= lastInventoryScanTime)
                    {
                        ScanInventoryForPlatinum();
                    }

                    // Only scan backpack at regular intervals to reduce CPU usage
                    if ((now - lastScanTime) >= LOOT_SCAN_INTERVAL)
                    {
                        lastScanTime = now;

                        // Only scan if we have templates and the game window
                        if (lootTemplates.Count > 0 && targetWindow != IntPtr.Zero)
                        {
                            ScanBackpackForLoot();
                        }
                    }

                    // Sleep to reduce CPU usage
                    Sleep(200);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LOOT] Error in loot recognizer: {ex.Message}");
                    Sleep(1000); // Sleep longer after an error
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LOOT] Error initializing loot recognizer: {ex.Message}");
        }

        //Console.WriteLine("[LOOT] Loot recognizer thread exited");
    }

    static void LoadLootTemplates()
    {
        try
        {
            // Clear any existing templates
            foreach (var template in lootTemplates.Values)
            {
                template.Dispose();
            }
            lootTemplates.Clear();

            // Get all image files from the drops directory
            string[] imageFiles = Directory.GetFiles(dropsDirectoryPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => IsImageFile(f))
                .ToArray();

            foreach (string filePath in imageFiles)
            {
                try
                {
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    //Console.WriteLine($"[LOOT] Loading item template: {Path.GetFileName(filePath)}");

                    // Load template image
                    Mat template = CvInvoke.Imread(filePath, ImreadModes.Color);

                    if (template.IsEmpty)
                    {
                        //Console.WriteLine($"[LOOT] Failed to load template: {Path.GetFileName(filePath)}");
                        continue;
                    }

                    // Store in dictionary with filename as key
                    lootTemplates[fileName] = template;

                    //Console.WriteLine($"[LOOT] Loaded {fileName}: {template.Width}x{template.Height}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LOOT] Error loading template {filePath}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LOOT] Error loading loot templates: {ex.Message}");
        }
    }

    // Check if a file is an image
    static bool IsImageFile(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLower();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";
    }

    // Scan the backpack slots for recognizable loot
    // Add this static flag at the class level, outside any functions
    private static bool hasDebugImageBeenSaved = false;
    static void ScanBackpackForLoot()
    {
        var debugMode = false;
        try
        {
            UpdateUIPositions();
            // Skip if we had a recent drag operation (avoid too frequent operations)
            if ((DateTime.Now - lastDragOperationTime) < DRAG_OPERATION_COOLDOWN)
            {
                return;
            }

            //Console.WriteLine("[LOOT] Starting backpack scan");

            // Debug directory and path variables
            string debugDir = string.Empty;
            if (debugMode)
            {
                // Output current values for debugging
                //Console.WriteLine($"[LOOT DEBUG] secondSlotBpX = {secondSlotBpX}, secondSLotBpY = {secondSLotBpY}");
                //Console.WriteLine($"[LOOT DEBUG] pixelSize = {pixelSize}");

                // Create timestamped debug directory to save all matches
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                debugDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug", $"scan_{timestamp}");
                if (!Directory.Exists(debugDir)) Directory.CreateDirectory(debugDir);
                //Console.WriteLine($"[LOOT DEBUG] Debug images will be saved to: {debugDir}");
            }

            // Get window dimensions
            GetClientRect(targetWindow, out RECT windowRect);
            int windowWidth = windowRect.Right - windowRect.Left;
            int windowHeight = windowRect.Bottom - windowRect.Top;


            //Console.WriteLine($"[LOOT DEBUG] Window dimensions: {windowWidth}x{windowHeight}");

            // Calculate scan area
            int backpackScanLeft = firstSlotBpX - (pixelSize / 2);
            int backpackScanTop = firstSlotBpY - (pixelSize / 2);
            int backpackScanWidth = pixelSize * 4;
            int backpackScanHeight = pixelSize * 2;



            // Ensure we're not capturing outside the window
            if (backpackScanLeft < 0) backpackScanLeft = 0;
            if (backpackScanTop < 0) backpackScanTop = 0;
            if (backpackScanLeft + backpackScanWidth > windowWidth) backpackScanWidth = windowWidth - backpackScanLeft;
            if (backpackScanTop + backpackScanHeight > windowHeight) backpackScanHeight = windowHeight - backpackScanTop;



            // Take a screenshot of the scan area
            using (Mat backpackArea = CaptureGameAreaAsMat(targetWindow, backpackScanLeft, backpackScanTop, backpackScanWidth, backpackScanHeight))
            {
                if (backpackArea == null || backpackArea.IsEmpty)
                {
                    //Console.WriteLine("[LOOT ERROR] Failed to capture backpack area");
                    return;
                }

                // Save debug images only if debug mode is enabled
                if (debugMode)
                {
                    // Always save the current scan area
                    string scanAreaPath = Path.Combine(debugDir, "backpack_scan_area.png");
                    CvInvoke.Imwrite(scanAreaPath, backpackArea);
                    //Console.WriteLine($"[LOOT DEBUG] Saved current backpack scan image to {scanAreaPath}");

                    // Get image dimensions
                    //Console.WriteLine($"[LOOT DEBUG] Captured image dimensions: {backpackArea.Width}x{backpackArea.Height}");

                    // Save all templates for reference
                    string templatesDir = Path.Combine(debugDir, "templates");
                    if (!Directory.Exists(templatesDir)) Directory.CreateDirectory(templatesDir);
                    foreach (var template in lootTemplates)
                    {
                        string templatePath = Path.Combine(templatesDir, $"{template.Key}.png");
                        CvInvoke.Imwrite(templatePath, template.Value);
                    }
                    //Console.WriteLine($"[LOOT DEBUG] Saved all {lootTemplates.Count} templates to {templatesDir}");
                }

                // Create a visualization image if in debug mode
                Mat visualizationImage = debugMode ? backpackArea.Clone() : null;

                try
                {
                    // Try to match each template
                    foreach (var template in lootTemplates)
                    {
                        string itemName = template.Key;
                        Mat itemTemplate = template.Value;

                        // Log template details if debugging

                        //Console.WriteLine($"[LOOT DEBUG] Trying to match template: {itemName} ({itemTemplate.Width}x{itemTemplate.Height})");

                        using (Mat result = new Mat())
                        {
                            // Perform template matching
                            CvInvoke.MatchTemplate(backpackArea, itemTemplate, result, TemplateMatchingType.CcoeffNormed);

                            double minVal = 0, maxVal = 0;
                            Point minLoc = new Point(), maxLoc = new Point();
                            CvInvoke.MinMaxLoc(result, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

                            // Log the best match value if debugging

                            //Console.WriteLine($"[LOOT DEBUG] Template {itemName} best match: {maxVal:F3} at ({maxLoc.X}, {maxLoc.Y})");

                            // Save debug images and visualization only if debug mode is on
                            if (debugMode)
                            {
                                // Save the result matrix to visualize correlation scores
                                using (Mat normalizedResult = new Mat())
                                {
                                    CvInvoke.Normalize(result, normalizedResult, 0, 255, NormType.MinMax);
                                    // Fixed version with proper parameters
                                    CvInvoke.ConvertScaleAbs(normalizedResult, normalizedResult, 1.0, 0.0);
                                    string resultPath = Path.Combine(debugDir, $"match_result_{itemName}.png");
                                    CvInvoke.Imwrite(resultPath, normalizedResult);
                                }

                                // Draw rectangle on visualization image (red for below threshold, green for match)
                                Rectangle matchRect = new Rectangle(maxLoc, new Size(itemTemplate.Width, itemTemplate.Height));
                                MCvScalar color = (maxVal >= lootMatchThreshold) ?
                                                new MCvScalar(0, 255, 0) :  // Green for good match
                                                new MCvScalar(0, 0, 255);   // Red for below threshold

                                CvInvoke.Rectangle(visualizationImage, matchRect, color, 2);

                                // Add text with match score
                                string matchText = $"{itemName}: {maxVal:F3}";
                                Point textPoint = new Point(matchRect.X, Math.Max(0, matchRect.Y - 5));
                                CvInvoke.PutText(visualizationImage, matchText, textPoint,
                                                FontFace.HersheyComplex, 0.5, color, 1);

                                // For good matches, save the matched region
                                if (maxVal >= 0.5) // Lower threshold for debugging
                                {
                                    // Extract matched region
                                    using (Mat matchedRegion = new Mat(backpackArea, matchRect))
                                    {
                                        string matchedRegionPath = Path.Combine(debugDir, $"matched_region_{itemName}_{maxVal:F3}.png");
                                        CvInvoke.Imwrite(matchedRegionPath, matchedRegion);
                                    }
                                }
                            }

                            // If we found a good match
                            if (maxVal >= lootMatchThreshold)
                            {
                                //Console.WriteLine($"[LOOT] Recognized item '{itemName}' in backpack (match: {maxVal:F3})");

                                // Debug visualization if enabled
                                if (debugMode)
                                {
                                    // Save final visualization before performing action
                                    string visualizationPath = Path.Combine(debugDir, "all_matches_visualization.png");
                                    CvInvoke.Imwrite(visualizationPath, visualizationImage);

                                    // Save a full screenshot with source and destination points marked
                                    using (Mat fullScreenshot = CaptureGameAreaAsMat(targetWindow, 0, 0, windowWidth, windowHeight))
                                    {
                                        if (fullScreenshot != null && !fullScreenshot.IsEmpty)
                                        {
                                            // Calculate the loot item coordinates in full screen space
                                            int itemX = backpackScanLeft + maxLoc.X + (itemTemplate.Width / 2);
                                            int itemY = backpackScanTop + maxLoc.Y + (itemTemplate.Height / 2);

                                            // Calculate inventory slot destination
                                            int invX = inventoryX;
                                            int invY = inventoryY + pixelSize;

                                            // Draw source and destination points
                                            CvInvoke.Circle(fullScreenshot, new Point(itemX, itemY), 10, new MCvScalar(0, 0, 255), 2);
                                            CvInvoke.Circle(fullScreenshot, new Point(invX, invY), 10, new MCvScalar(0, 255, 0), 2);
                                            CvInvoke.Line(fullScreenshot, new Point(itemX, itemY), new Point(invX, invY),
                                                        new MCvScalar(255, 255, 0), 2, LineType.AntiAlias);

                                            string dragPath = Path.Combine(debugDir, "drag_visualization.png");
                                            CvInvoke.Imwrite(dragPath, fullScreenshot);
                                        }
                                    }
                                }

                                // Wait a moment to ensure the game is ready
                                Sleep(1);

                                // Calculate where in the slot the match occurred
                                int lootItemX = backpackScanLeft + maxLoc.X + (itemTemplate.Width / 2);
                                int lootItemY = backpackScanTop + maxLoc.Y + (itemTemplate.Height / 2);

                                // Log exact match coordinates if debugging

                                //Console.WriteLine($"[LOOT DEBUG] Loot item center at screen coordinates: ({lootItemX}, {lootItemY})");

                                // Calculate inventory slot destination
                                int inventorySlotX = inventoryX;
                                int inventorySlotY = inventoryY + pixelSize + 30;


                                //Console.WriteLine($"[LOOT DEBUG] Inventory slot coordinates: ({inventorySlotX}, {inventorySlotY})");

                                // Check if the recognized template is "100gold"
                                if (itemName.Equals("100gold", StringComparison.OrdinalIgnoreCase))
                                {
                                    // If it's gold, just right-click it instead of dragging
                                    //Console.WriteLine($"[LOOT] Found gold! Right-clicking instead of dragging");
                                    VirtualRightClick(targetWindow, lootItemX, lootItemY);

                                    // Update the last operation time
                                    lastDragOperationTime = DateTime.Now;
                                }
                                else
                                {
                                    // Calculate inventory slot destination for other items
                                    int invX = inventoryX;
                                    int invY = inventoryY + pixelSize + 30;
                                    if (itemName.Equals("stealthring", StringComparison.OrdinalIgnoreCase))
                                    {
                                        invY = inventoryY;
                                    }
                                    else if (itemName.Equals("lifering", StringComparison.OrdinalIgnoreCase))
                                    {
                                        invY = inventoryY + 3 * pixelSize + 15;
                                    }

                                    // Drag the item to the destination
                                    DragItemToDestination(lootItemX, lootItemY, invX, invY, itemName);

                                    // Update the last drag operation time
                                    lastDragOperationTime = DateTime.Now;
                                }

                                // Update the last drag operation time
                                lastDragOperationTime = DateTime.Now;

                                // Only process one item at a time
                                break;
                            }
                        }
                    }

                    // Save final visualization with all matches if in debug mode
                    if (debugMode && visualizationImage != null)
                    {
                        string allMatchesPath = Path.Combine(debugDir, "all_matches_visualization.png");
                        CvInvoke.Imwrite(allMatchesPath, visualizationImage);
                        //Console.WriteLine($"[LOOT DEBUG] Saved visualization with all matches to {allMatchesPath}");
                    }
                }
                finally
                {
                    // Clean up visualization image if created
                    if (visualizationImage != null)
                    {
                        visualizationImage.Dispose();
                    }
                }
            }

            //Console.WriteLine("[LOOT] Scan complete");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LOOT ERROR] Error scanning backpack: {ex.Message}");
            Console.WriteLine($"[LOOT ERROR] Stack trace: {ex.StackTrace}");
        }
    }


    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
    IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    const uint SRCCOPY = 0x00CC0020;

    // Capture a specific area of the game window as a Mat object
    static Mat CaptureGameAreaAsMat(IntPtr hWnd, int x, int y, int width, int height)
    {
        IntPtr hdcWindow = IntPtr.Zero;
        IntPtr hdcMemDC = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;
        Mat result = null;

        try
        {
            hdcWindow = GetDC(hWnd);
            if (hdcWindow == IntPtr.Zero)
            {
                //Console.WriteLine("[LOOT] GetDC failed");
                return null;
            }

            hdcMemDC = CreateCompatibleDC(hdcWindow);
            if (hdcMemDC == IntPtr.Zero)
            {
                //Console.WriteLine("[LOOT] CreateCompatibleDC failed");
                return null;
            }

            hBitmap = CreateCompatibleBitmap(hdcWindow, width, height);
            if (hBitmap == IntPtr.Zero)
            {
                //Console.WriteLine("[LOOT] CreateCompatibleBitmap failed");
                return null;
            }

            hOld = SelectObject(hdcMemDC, hBitmap);

            bool success = BitBlt(hdcMemDC, 0, 0, width, height, hdcWindow, x, y, SRCCOPY);
            if (!success)
            {
                //Console.WriteLine("[LOOT] BitBlt failed");
                return null;
            }

            SelectObject(hdcMemDC, hOld);

            // Convert the bitmap to a Mat
            using (Bitmap bmp = Bitmap.FromHbitmap(hBitmap))
            {
                // Convert Bitmap to Mat
                Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

                try
                {
                    // Create a Mat directly from the bitmap data
                    result = new Mat(bmp.Height, bmp.Width, DepthType.Cv8U, 3, bmpData.Scan0, bmpData.Stride);
                    // Create a deep copy to keep the Mat alive after unlocking the bitmap
                    result = result.Clone();
                }
                finally
                {
                    bmp.UnlockBits(bmpData);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LOOT] Screenshot error: {ex.Message}");
            result?.Dispose();
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (hdcMemDC != IntPtr.Zero) DeleteDC(hdcMemDC);
            if (hdcWindow != IntPtr.Zero) ReleaseDC(hWnd, hdcWindow);
        }
    }

    static bool shouldScanInventoryForPlatinum = false;
    static readonly string PLATINUM_TEMPLATE_NAME = "100platinum";
    static readonly TimeSpan PLATINUM_SCAN_DELAY = TimeSpan.FromMilliseconds(1500); // Delay before scanning inventory for platinum
    static DateTime lastInventoryScanTime = DateTime.MinValue;

    static void DragItemToDestination(int sourceX, int sourceY, int destX, int destY, string itemName)
    {
        try
        {
            //Console.WriteLine($"[LOOT] Dragging '{itemName}' from ({sourceX}, {sourceY}) to ({destX}, {destY})");

            // Create IntPtr lParam values for source and destination
            IntPtr sourceLParam = MakeLParam(sourceX, sourceY);
            IntPtr destLParam = MakeLParam(destX, destY);

            // Log the lParam values for debugging
            //Console.WriteLine($"[LOOT DEBUG] Source lParam: 0x{sourceLParam.ToInt64():X}, dest lParam: 0x{destLParam.ToInt64():X}");

            // Move mouse to source position
            //Console.WriteLine($"[LOOT DEBUG] Moving mouse to source position");
            SendMessage(targetWindow, WM_MOUSEMOVE, IntPtr.Zero, sourceLParam);
            Sleep(1);  // Short delay between operations

            // Left button down at source
            //Console.WriteLine($"[LOOT DEBUG] Mouse button down at source");
            SendMessage(targetWindow, WM_LBUTTONDOWN, IntPtr.Zero, sourceLParam);
            Sleep(1);  // Short delay

            // Record the source click for overlay visualization
            RecordClickPosition(sourceX, sourceY, true);

            // Move mouse to destination with button held down
            //Console.WriteLine($"[LOOT DEBUG] Moving to destination with button held");
            SendMessage(targetWindow, WM_MOUSEMOVE, new IntPtr(MK_LBUTTON), destLParam);
            Sleep(1);  // Short delay

            // Release button at destination
            //Console.WriteLine($"[LOOT DEBUG] Releasing button at destination");
            SendMessage(targetWindow, WM_LBUTTONUP, IntPtr.Zero, destLParam);
            Sleep(1);  // Short delay

            // Record the destination click for overlay visualization
            RecordClickPosition(destX, destY, true);

            // Add a significant delay to allow game to process the action
            //Console.WriteLine($"[LOOT DEBUG] Drag operation completed, waiting for game to process...");
            Sleep(1);  // This delay is important for the game to process the action

            // NEW: Flag for inventory scanning if this was a platinum item
            if (itemName.Equals(PLATINUM_TEMPLATE_NAME, StringComparison.OrdinalIgnoreCase))
            {
                //Console.WriteLine($"[LOOT] Detected platinum move to inventory. Will scan inventory for right-click.");
                shouldScanInventoryForPlatinum = true;
                lastInventoryScanTime = DateTime.Now.Add(PLATINUM_SCAN_DELAY); // Set time for future scan
            }

            //Console.WriteLine($"[LOOT] Successfully moved item '{itemName}' to inventory");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LOOT ERROR] Error dragging item: {ex.Message}");
            Console.WriteLine($"[LOOT ERROR] Stack trace: {ex.StackTrace}");
        }
    }

    static void ScanInventoryForPlatinum()
    {
        try
        {
            //Console.WriteLine("[LOOT] Scanning inventory for platinum coins");

            // Calculate scan area - focused on inventory area where the coins were dropped
            int invScanLeft = inventoryX - (pixelSize / 2);
            int invScanTop = inventoryY - (pixelSize / 2);
            int invScanWidth = pixelSize * 2;  // Adjusted width to cover inventory area
            int invScanHeight = pixelSize * 3; // Taller height to cover more inventory slots

            //Console.WriteLine($"[LOOT] Inventory scan area: X={invScanLeft}, Y={invScanTop}, Width={invScanWidth}, Height={invScanHeight}");

            // Ensure we're not capturing outside the window
            GetClientRect(targetWindow, out RECT windowRect);
            int windowWidth = windowRect.Right - windowRect.Left;
            int windowHeight = windowRect.Bottom - windowRect.Top;

            if (invScanLeft < 0) invScanLeft = 0;
            if (invScanTop < 0) invScanTop = 0;
            if (invScanLeft + invScanWidth > windowWidth) invScanWidth = windowWidth - invScanLeft;
            if (invScanTop + invScanHeight > windowHeight) invScanHeight = windowHeight - invScanTop;

            // Take a screenshot of the inventory area
            using (Mat inventoryArea = CaptureGameAreaAsMat(targetWindow, invScanLeft, invScanTop, invScanWidth, invScanHeight))
            {
                if (inventoryArea == null || inventoryArea.IsEmpty)
                {
                    //Console.WriteLine("[LOOT ERROR] Failed to capture inventory area");
                    return;
                }

                // Find the platinum template
                if (lootTemplates.TryGetValue(PLATINUM_TEMPLATE_NAME, out Mat platinumTemplate))
                {
                    using (Mat result = new Mat())
                    {
                        // Perform template matching
                        CvInvoke.MatchTemplate(inventoryArea, platinumTemplate, result, TemplateMatchingType.CcoeffNormed);

                        double minVal = 0, maxVal = 0;
                        Point minLoc = new Point(), maxLoc = new Point();
                        CvInvoke.MinMaxLoc(result, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

                        //Console.WriteLine($"[LOOT] Platinum match in inventory: {maxVal:F3} at ({maxLoc.X}, {maxLoc.Y})");

                        // If we found a good match for platinum in the inventory
                        if (maxVal >= lootMatchThreshold)
                        {
                            // Calculate where in the inventory the match occurred
                            int platinumX = invScanLeft + maxLoc.X + (platinumTemplate.Width / 2);
                            int platinumY = invScanTop + maxLoc.Y + (platinumTemplate.Height / 2);

                            //Console.WriteLine($"[LOOT] Found platinum in inventory at ({platinumX}, {platinumY}) - right-clicking");

                            // Right click on the platinum coins
                            VirtualRightClick(targetWindow, platinumX, platinumY);

                            // Reset the flag since we've processed the platinum
                            shouldScanInventoryForPlatinum = false;

                            //Console.WriteLine("[LOOT] Successfully right-clicked on platinum coins in inventory");
                        }
                        else
                        {
                            //Console.WriteLine("[LOOT] Could not find platinum in inventory with sufficient confidence");
                        }
                    }
                }
                else
                {
                    //Console.WriteLine($"[LOOT ERROR] Platinum template '{PLATINUM_TEMPLATE_NAME}' not found in loaded templates");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LOOT ERROR] Error scanning inventory for platinum: {ex.Message}");
        }
        finally
        {
            // Always reset the flag to prevent infinite retries
            shouldScanInventoryForPlatinum = false;
        }
    }

    static readonly TimeSpan RING_OPERATION_COOLDOWN = TimeSpan.FromSeconds(2); // Cooldown between ring movements
    static DateTime lastRingOperationTime = DateTime.MinValue;
    // Scan the ring containers for misplaced rings
    // Add this static variable at the class level, near other timing variables
    static DateTime lastRingScanTime = DateTime.MinValue;
    static readonly TimeSpan RING_SCAN_INTERVAL = TimeSpan.FromSeconds(1); // Scan rings at most once per second

    // Modified ScanRingContainersForMisplacedRings function
    static void ScanRingContainersForMisplacedRings()
    {
        try
        {
            // Check if enough time has passed since last scan
            DateTime now = DateTime.Now;
            if ((now - lastRingScanTime) < RING_SCAN_INTERVAL)
            {
                // Not enough time has passed, skip this scan
                return;
            }

            // Update the last scan time
            lastRingScanTime = now;

            //Console.WriteLine("[RING CORRECTOR] Starting scan for misplaced rings in ring containers and backpack");

            // Update UI positions to ensure we have the correct coordinates
            UpdateUIPositions();

            // Define the ring slot positions based on ToggleRing function
            // Stealth ring slot (top slot)
            int stealthRingSlotX = inventoryX;
            int stealthRingSlotY = inventoryY;

            // Life ring slot (bottom slot) - same formula as in ToggleRing
            int lifeRingSlotX = inventoryX;
            int lifeRingSlotY = inventoryY + 3 * pixelSize + 15;

            //Console.WriteLine($"[RING CORRECTOR] Stealth ring slot: ({stealthRingSlotX}, {stealthRingSlotY})");
            //Console.WriteLine($"[RING CORRECTOR] Life ring slot: ({lifeRingSlotX}, {lifeRingSlotY})");

            // First check the ring slots
            bool stealthRingInLifeSlot = ScanSlotForItem("stealthring", lifeRingSlotX, lifeRingSlotY);
            bool lifeRingInStealthSlot = ScanSlotForItem("lifering", stealthRingSlotX, stealthRingSlotY);

            // Also check the first 4 slots of the backpack for rings
            //Console.WriteLine("[RING CORRECTOR] Checking first 4 slots of backpack for rings");
            var stealthRingInBackpack = ScanSlotsForItemWithPosition("stealthring", lifeRingSlotX, lifeRingSlotY, 4);
            var lifeRingInBackpack = ScanSlotsForItemWithPosition("lifering", stealthRingSlotX, stealthRingSlotY, 4);

            // Process findings
            // If rings are in wrong equipment slots, prioritize swapping them
            if (stealthRingInLifeSlot && lifeRingInStealthSlot)
            {
                //Console.WriteLine("[RING CORRECTOR] Both rings are in wrong positions - swapping them");
                SwapRings(stealthRingSlotX, stealthRingSlotY, lifeRingSlotX, lifeRingSlotY);
            }
            else if (stealthRingInLifeSlot)
            {
                //Console.WriteLine("[RING CORRECTOR] Stealth ring in life ring slot - moving to correct position");
                DragItemToDestination(lifeRingSlotX, lifeRingSlotY, stealthRingSlotX, stealthRingSlotY, "stealthring_correction");
                lastRingOperationTime = DateTime.Now;
            }
            else if (lifeRingInStealthSlot)
            {
                //Console.WriteLine("[RING CORRECTOR] Life ring in stealth ring slot - moving to correct position");
                DragItemToDestination(stealthRingSlotX, stealthRingSlotY, lifeRingSlotX, lifeRingSlotY, "lifering_correction");
                lastRingOperationTime = DateTime.Now;
            }
            // If rings are found in backpack, move them to their correct slots
            else if (stealthRingInBackpack.found)
            {
                //Console.WriteLine($"[RING CORRECTOR] Stealth ring found in backpack at ({stealthRingInBackpack.slotX}, {stealthRingInBackpack.slotY}) - moving to equipment slot");
                DragItemToDestination(stealthRingInBackpack.slotX, stealthRingInBackpack.slotY, stealthRingSlotX, stealthRingSlotY, "stealthring_from_backpack");
                lastRingOperationTime = DateTime.Now;
            }
            else if (lifeRingInBackpack.found)
            {
                //Console.WriteLine($"[RING CORRECTOR] Life ring found in backpack at ({lifeRingInBackpack.slotX}, {lifeRingInBackpack.slotY}) - moving to equipment slot");
                DragItemToDestination(lifeRingInBackpack.slotX, lifeRingInBackpack.slotY, lifeRingSlotX, lifeRingSlotY, "lifering_from_backpack");
                lastRingOperationTime = DateTime.Now;
            }
            else
            {
                //Console.WriteLine("[RING CORRECTOR] All rings are in correct positions");
            }

            //Console.WriteLine("[RING CORRECTOR] Scan complete");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RING CORRECTOR] Error scanning ring containers: {ex.Message}");
        }
    }

    // Scan a specific slot for a specific item
    static bool ScanSlotForItem(string itemName, int slotX, int slotY, int numSlotsToScan = 1)
    {
        try
        {
            // For backpack scanning, we need to check multiple slots
            // If numSlotsToScan > 1, scan that many slots to the right

            for (int slotIndex = 0; slotIndex < numSlotsToScan; slotIndex++)
            {
                // Calculate the center of the current slot
                int currentSlotX = slotX + (slotIndex * pixelSize);
                int currentSlotY = slotY;

                // Calculate scan area around the slot center
                int scanSize = pixelSize / 2;
                int scanLeft = currentSlotX - scanSize;
                int scanTop = currentSlotY - scanSize;
                int scanWidth = pixelSize;
                int scanHeight = pixelSize;

                // Ensure we're not capturing outside the window
                GetClientRect(targetWindow, out RECT windowRect);
                int windowWidth = windowRect.Right - windowRect.Left;
                int windowHeight = windowRect.Bottom - windowRect.Top;

                if (scanLeft < 0) scanLeft = 0;
                if (scanTop < 0) scanTop = 0;
                if (scanLeft + scanWidth > windowWidth) scanWidth = windowWidth - scanLeft;
                if (scanTop + scanHeight > windowHeight) scanHeight = windowHeight - scanTop;

                // Capture the area
                using (Mat slotArea = CaptureGameAreaAsMat(targetWindow, scanLeft, scanTop, scanWidth, scanHeight))
                {
                    if (slotArea == null || slotArea.IsEmpty)
                    {
                        continue; // Try next slot
                    }

                    // Check if the specific item template exists
                    if (lootTemplates.TryGetValue(itemName, out Mat itemTemplate))
                    {
                        using (Mat result = new Mat())
                        {
                            CvInvoke.MatchTemplate(slotArea, itemTemplate, result, TemplateMatchingType.CcoeffNormed);

                            double minVal = 0, maxVal = 0;
                            Point minLoc = new Point(), maxLoc = new Point();
                            CvInvoke.MinMaxLoc(result, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

                            //Console.WriteLine($"[RING CORRECTOR] {itemName} match value at slot {slotIndex}: {maxVal:F3}");

                            // If we found a good match, return true immediately
                            if (maxVal >= lootMatchThreshold)
                            {
                                //Console.WriteLine($"[RING CORRECTOR] Found {itemName} in slot {slotIndex} (X: {currentSlotX}, Y: {currentSlotY})");
                                return true;
                            }
                        }
                    }
                }
            }

            return false; // Item not found in any of the scanned slots
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RING CORRECTOR] Error scanning slots for {itemName}: {ex.Message}");
            return false;
        }
    }

    // Modified version that also returns the position where the item was found
    static (bool found, int slotX, int slotY) ScanSlotsForItemWithPosition(string itemName, int startSlotX, int startSlotY, int numSlotsToScan = 1)
    {
        try
        {
            for (int slotIndex = 0; slotIndex < numSlotsToScan; slotIndex++)
            {
                // Calculate the center of the current slot
                int currentSlotX = startSlotX + (slotIndex * pixelSize);
                int currentSlotY = startSlotY;

                // Calculate scan area around the slot center
                int scanSize = pixelSize / 2;
                int scanLeft = currentSlotX - scanSize;
                int scanTop = currentSlotY - scanSize;
                int scanWidth = pixelSize;
                int scanHeight = pixelSize;

                // Ensure we're not capturing outside the window
                GetClientRect(targetWindow, out RECT windowRect);
                int windowWidth = windowRect.Right - windowRect.Left;
                int windowHeight = windowRect.Bottom - windowRect.Top;

                if (scanLeft < 0) scanLeft = 0;
                if (scanTop < 0) scanTop = 0;
                if (scanLeft + scanWidth > windowWidth) scanWidth = windowWidth - scanLeft;
                if (scanTop + scanHeight > windowHeight) scanHeight = windowHeight - scanTop;

                // Capture the area
                using (Mat slotArea = CaptureGameAreaAsMat(targetWindow, scanLeft, scanTop, scanWidth, scanHeight))
                {
                    if (slotArea == null || slotArea.IsEmpty)
                    {
                        continue; // Try next slot
                    }

                    // Check if the specific item template exists
                    if (lootTemplates.TryGetValue(itemName, out Mat itemTemplate))
                    {
                        using (Mat result = new Mat())
                        {
                            CvInvoke.MatchTemplate(slotArea, itemTemplate, result, TemplateMatchingType.CcoeffNormed);

                            double minVal = 0, maxVal = 0;
                            Point minLoc = new Point(), maxLoc = new Point();
                            CvInvoke.MinMaxLoc(result, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

                            //Console.WriteLine($"[RING CORRECTOR] {itemName} match value at slot {slotIndex}: {maxVal:F3}");

                            // If we found a good match, return position
                            if (maxVal >= lootMatchThreshold)
                            {
                                //Console.WriteLine($"[RING CORRECTOR] Found {itemName} in slot {slotIndex} (X: {currentSlotX}, Y: {currentSlotY})");
                                return (true, currentSlotX, currentSlotY);
                            }
                        }
                    }
                }
            }

            return (false, 0, 0); // Item not found in any of the scanned slots
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RING CORRECTOR] Error scanning slots for {itemName}: {ex.Message}");
            return (false, 0, 0);
        }
    }

    // Swap two rings between their slots
    static void SwapRings(int slot1X, int slot1Y, int slot2X, int slot2Y)
    {
        try
        {
            // We need a temporary location to move one ring while swapping
            // Use first backpack slot as temporary storage
            int tempX = firstSlotBpX;
            int tempY = firstSlotBpY;

            //Console.WriteLine("[RING CORRECTOR] Starting ring swap operation");

            // Step 1: Move ring from slot1 to temporary location
            DragItemToDestination(slot1X, slot1Y, tempX, tempY, "ring_to_temp");
            Sleep(1);

            // Step 2: Move ring from slot2 to slot1
            DragItemToDestination(slot2X, slot2Y, slot1X, slot1Y, "ring_to_slot1");
            Sleep(1);

            // Step 3: Move ring from temporary location to slot2
            DragItemToDestination(tempX, tempY, slot2X, slot2Y, "ring_to_slot2");

            lastRingOperationTime = DateTime.Now;
            //Console.WriteLine("[RING CORRECTOR] Ring swap completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RING CORRECTOR] Error swapping rings: {ex.Message}");
        }
    }

}
