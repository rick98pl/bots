using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
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
    const int wp_MAX_WAIT_TIME_MS = 2500;
    public static readonly int VK_F4 = 0x73;
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
    const int DEFAULT_HP_THRESHOLD = 40;
    const int DEFAULT_BEEP_HP_THRESHOLD = 30;
    const int DEFAULT_MANA_THRESHOLD = -1;
    const string DEFAULT_HP_KEY_NAME = "F1";
    const string DEFAULT_MANA_KEY_NAME = "F2";
    static int DEFAULT_HP_KEY => Keys.GetKeyCode(DEFAULT_HP_KEY_NAME);
    static int DEFAULT_MANA_KEY => Keys.GetKeyCode(DEFAULT_MANA_KEY_NAME);
    static IntPtr targetWindow = IntPtr.Zero;
    static DateTime lastHpActionTime = DateTime.MinValue;
    static DateTime lastManaActionTime = DateTime.MinValue;
    static Random random = new Random();
    static IntPtr xAddressOffset = 0x009435FC;
    static IntPtr yAddressOffset = 0x00943600;
    static IntPtr zAddressOffset = 0x00943604;
    static IntPtr targetIdOffset = 0x009432D4;
    static IntPtr followOffset = 0x00943380;
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
    static bool shouldRestartMemoryThread = false;
    private static int lastKnownMonsterX = 0;
    private static int lastKnownMonsterY = 0;
    private static int lastKnownMonsterZ = 0;
    static ConcurrentDictionary<string, bool> threadFlags = new ConcurrentDictionary<
        string,
        bool
    >();
    static object memoryLock = new object();
    static List<Coordinate> recordedCoords = new List<Coordinate>();
    static string cordsFilePath = "cords.json";
    static CoordinateData? loadedCoords = null;
    static Process? selectedProcess = null;
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
    static Coordinate? currentTarget = null;
    static bool debug = true;
    static int debugTime = 2;
    static void Main()
    {
        threadFlags["recording"] = false;
        threadFlags["playing"] = false;
        threadFlags["autopot"] = true;
        threadFlags["spawnwatch"] = true;
        threadFlags["lootrecognizer"] = true;
        threadFlags.TryAdd("overlay", true);
        if (!threadFlags.ContainsKey("overlay"))
        {
            threadFlags.TryAdd("overlay", false);
        }
        if (!threadFlags.ContainsKey("clickoverlay"))
        {
            threadFlags.TryAdd("clickoverlay", false);
        }
        InitializeSounds();
        if (!File.Exists(cordsFilePath))
        {
            SaveCoordinates();
        }
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
                        Sleep(2000);
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
            smallWindow = windowHeight < 1200;
            StartWorkerThreads();
            DisplayStats();
            DateTime lastOverlayCheck = DateTime.MinValue;
            DateTime lastInputCheck = DateTime.MinValue;
            const int INPUT_CHECK_INTERVAL = 50;
            const int OVERLAY_CHECK_INTERVAL = 2000;
            while (memoryReadActive && !shouldRestartMemoryThread)
            {
                DateTime now = DateTime.Now;
                if ((now - lastInputCheck).TotalMilliseconds >= INPUT_CHECK_INTERVAL)
                {
                    lastInputCheck = now;
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true).Key;
                        HandleUserInput(key);
                    }
                }
                if ((now - lastOverlayCheck).TotalMilliseconds >= OVERLAY_CHECK_INTERVAL)
                {
                    lastOverlayCheck = now;
                    if (threadFlags["overlay"] && (overlayForm == null || overlayForm.IsDisposed))
                    {
                        StartOverlay();
                    }
                    else if (!threadFlags["overlay"] && overlayForm != null && !overlayForm.IsDisposed)
                    {
                        StopOverlay();
                    }
                }
                Sleep(50);
            }
            if (shouldRestartMemoryThread)
            {
                shouldRestartMemoryThread = false;
                StopWorkerThreads();
                StopOverlay();
                selectedProcess = null;
            }
        }
    }
    static bool maintainOutfitActive = true;
    static int desiredOutfit = 75;
    static Thread? outfitThread = null;
    static readonly object outfitLock = new object();
    static void MaintainOutfitThread()
    {
        const int CHECK_INTERVAL_MS = 500;
        DateTime lastCheckTime = DateTime.MinValue;
        try
        {
            int currentOutfitValue;
            lock (memoryLock)
            {
                currentOutfitValue = currentOutfit;
            }
            if (currentOutfitValue != desiredOutfit)
            {
                IntPtr outfitAddress = IntPtr.Zero;
                foreach (var variable in variables)
                {
                    if (variable.Name.Contains("currentOutfit"))
                    {
                        IntPtr baseAddress = IntPtr.Add(moduleBase, (int)variable.BaseAddress);
                        byte[] buffer = new byte[4];
                        if (!ReadProcessMemory(processHandle, baseAddress, buffer, buffer.Length, out _))
                        {
                            return;
                        }
                        outfitAddress = BitConverter.ToInt32(buffer, 0);
                        outfitAddress = IntPtr.Add(outfitAddress, variable.Offsets[0]);
                        break;
                    }
                }
                if (outfitAddress == IntPtr.Zero)
                {
                    return;
                }
                byte[] exactOutfitBuffer = BitConverter.GetBytes(desiredOutfit);
                int bytesWritten;
                if (!WriteProcessMemory(processHandle, outfitAddress, exactOutfitBuffer, exactOutfitBuffer.Length, out bytesWritten))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    return;
                }
                currentOutfit = desiredOutfit;
                Sleep(250);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OUTFIT] Error during initial outfit setting: {ex.Message}");
        }
        while (memoryReadActive && maintainOutfitActive)
        {
            try
            {
                DateTime now = DateTime.Now;
                if ((now - lastCheckTime).TotalMilliseconds < CHECK_INTERVAL_MS)
                {
                    Sleep(50);
                    continue;
                }
                lastCheckTime = now;
                int currentOutfitValue;
                lock (memoryLock)
                {
                    currentOutfitValue = currentOutfit;
                }
                lock (outfitLock)
                {
                    if (currentOutfitValue != desiredOutfit)
                    {
                        IntPtr outfitAddress = IntPtr.Zero;
                        foreach (var variable in variables)
                        {
                            if (variable.Name.Contains("currentOutfit"))
                            {
                                IntPtr baseAddress = IntPtr.Add(moduleBase, (int)variable.BaseAddress);
                                byte[] buffer = new byte[4];
                                if (!ReadProcessMemory(processHandle, baseAddress, buffer, buffer.Length, out _))
                                {
                                    break;
                                }
                                outfitAddress = BitConverter.ToInt32(buffer, 0);
                                outfitAddress = IntPtr.Add(outfitAddress, variable.Offsets[0]);
                                break;
                            }
                        }
                        if (outfitAddress == IntPtr.Zero)
                        {
                            Sleep(1000);
                            continue;
                        }
                        byte[] exactOutfitBuffer = BitConverter.GetBytes(desiredOutfit);
                        int bytesWritten;
                        if (!WriteProcessMemory(processHandle, outfitAddress, exactOutfitBuffer, exactOutfitBuffer.Length, out bytesWritten))
                        {
                            int errorCode = Marshal.GetLastWin32Error();
                            Sleep(1000);
                            continue;
                        }
                        currentOutfit = desiredOutfit;
                        Sleep(50);
                    }
                }
                Sleep(200);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OUTFIT] Error in outfit maintenance: {ex.Message}");
                Sleep(1000);
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
        if (false)
        {
            Thread outfitThread = new Thread(MaintainOutfitThread);
            outfitThread.IsBackground = true;
            outfitThread.Name = "OutfitMaintenance";
            outfitThread.Start();
        }
        Thread lootRecognizerThread = new Thread(LootRecognizerThread);
        lootRecognizerThread.IsBackground = true;
        lootRecognizerThread.Name = "LootRecognizer";
        lootRecognizerThread.Start();
        StartClickOverlay();
        if (threadFlags["spawnwatch"])
        {
            SPAWNWATCHER.Start(targetWindow, pixelSize);
        }
    }
    static void StopWorkerThreads()
    {
        memoryReadActive = false;
        threadFlags["recording"] = false;
        threadFlags["playing"] = false;
        threadFlags["autopot"] = false;
        threadFlags["overlay"] = false;
        lootRecognizerActive = false;
        SPAWNWATCHER.Stop();
        StopPositionAlertSound();
        StopOverlay();
        StopClickOverlay();
        Sleep(1000);
    }
    static List<Variable> variables = new List<Variable>
    {
        new Variable
        {
            Name = "Current Mana",
            BaseAddress = 0x009432D0,
            Offsets = new List<int> { 1240 },
            Type = "Double"
        },
        new Variable
        {
            Name = "Current HP",
            BaseAddress = 0x009432D0,
            Offsets = new List<int> { 1184 },
            Type = "Double"
        },
        new Variable
        {
            Name = "Max Mana",
            BaseAddress = 0x009432D0,
            Offsets = new List<int> { 1248 },
            Type = "Double"
        },
        new Variable
        {
            Name = "Max HP",
            BaseAddress = 0x009432D0,
            Offsets = new List<int> { 1192 },
            Type = "Double"
        },
        new Variable
        {
            Name = "currentOutfit",
            BaseAddress = 0x009432D0,
            Offsets = new List<int> { 88 },
            Type = "Int32"
        },
        new Variable
        {
            Name = "invisibilityCode",
            BaseAddress = 0x009432D0,
            Offsets = new List<int> { 84 },
            Type = "Int32"
        }
    };
    static void MemoryReadingThread()
    {
        DateTime lastDebugOutputTime = DateTime.MinValue;
        DateTime lastFullScanTime = DateTime.MinValue;
        const double DEBUG_COOLDOWN_SECONDS = 1.5;
        const double FULL_SCAN_INTERVAL_MS = 100;
        int consecutiveFailures = 0;
        const int MAX_FAILURES = 5;
        while (memoryReadActive)
        {
            try
            {
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
                            IntPtr address = IntPtr.Add(moduleBase, (int)variable.BaseAddress);
                            byte[] buffer;
                            if (variable.Offsets.Count > 0)
                            {
                                buffer = new byte[4];
                                if (!ReadProcessMemory(processHandle, address, buffer, buffer.Length, out _))
                                    continue;
                                address = BitConverter.ToInt32(buffer, 0);
                                address = IntPtr.Add(address, variable.Offsets[0]);
                            }
                            buffer = new byte[8];
                            if (!ReadProcessMemory(processHandle, address, buffer, buffer.Length, out _))
                                continue;
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
                        chaseTracker.Update(posX, posY, posZ, targetId);
                        if (debug && targetId != 0)
                        {
                            DateTime debugNow = DateTime.Now;
                            if ((debugNow - lastDebugOutputTime).TotalSeconds >= DEBUG_COOLDOWN_SECONDS)
                            {
                                lastDebugOutputTime = debugNow;
                            }
                        }
                    }
                    if (loadedCoords != null && loadedCoords.cords.Count > 0 && threadFlags["playing"])
                    {
                        CheckPositionDistance(loadedCoords.cords);
                    }
                    consecutiveFailures = 0;
                }
                else
                {
                    lock (memoryLock)
                    {
                        posX = ReadInt32(processHandle, moduleBase, xAddressOffset);
                        posY = ReadInt32(processHandle, moduleBase, yAddressOffset);
                        targetId = ReadInt32(processHandle, moduleBase, targetIdOffset);
                    }
                }
                int sleepTime = (targetId == 0) ? 50 : 20;
                Sleep(sleepTime);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Memory reading error: {ex.Message}");
                consecutiveFailures++;
                if (consecutiveFailures >= MAX_FAILURES)
                {
                    shouldRestartMemoryThread = true;
                    break;
                }
                Sleep(500);
            }
        }
    }
    static void ChangeOutfit(int change)
    {
        try
        {
            int currentOutfitVar;
            lock (memoryLock)
            {
                currentOutfitVar = currentOutfit;
            }
            int previousOutfit = currentOutfitVar;
            int newOutfit = previousOutfit + change;
            const int MIN_OUTFIT = 0;
            const int MAX_OUTFIT = 99999;
            if (newOutfit < MIN_OUTFIT)
                newOutfit = MIN_OUTFIT;
            if (newOutfit > MAX_OUTFIT)
                newOutfit = MAX_OUTFIT;
            if (newOutfit == previousOutfit)
            {
                return;
            }
            IntPtr outfitAddress = IntPtr.Zero;
            foreach (var variable in variables)
            {
                if (variable.Name.Contains("currentOutfit"))
                {
                    IntPtr baseAddress = IntPtr.Add(moduleBase, (int)variable.BaseAddress);
                    byte[] buffer = new byte[4];
                    if (!ReadProcessMemory(processHandle, baseAddress, buffer, buffer.Length, out _))
                    {
                        return;
                    }
                    outfitAddress = BitConverter.ToInt32(buffer, 0);
                    outfitAddress = IntPtr.Add(outfitAddress, variable.Offsets[0]);
                    break;
                }
            }
            if (outfitAddress == IntPtr.Zero)
            {
                return;
            }
            byte[] newOutfitBuffer = BitConverter.GetBytes(newOutfit);
            int bytesWritten;
            if (!WriteProcessMemory(processHandle, outfitAddress, newOutfitBuffer, newOutfitBuffer.Length, out bytesWritten))
            {
                int errorCode = Marshal.GetLastWin32Error();
                return;
            }
            currentOutfit = newOutfit;
            lock (outfitLock)
            {
                desiredOutfit = newOutfit;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error changing outfit: {ex.Message}");
        }
    }
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
        DateTime lastStatsDisplay = DateTime.MinValue;
        const int STATS_DISPLAY_INTERVAL_MS = 2000;
        while (memoryReadActive)
        {
            try
            {
                DateTime now = DateTime.Now;
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
                        Sleep(50);
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
                            SendKeyPress(DEFAULT_HP_KEY);
                            lastHpActionTime = now.AddMilliseconds(random.Next(0, 100));
                        }
                    }
                    if (manaPercent <= DEFAULT_MANA_THRESHOLD)
                    {
                        if ((now - lastManaActionTime).TotalMilliseconds >= thresholdms)
                        {
                            SendKeyPress(DEFAULT_MANA_KEY);
                            lastManaActionTime = now.AddMilliseconds(random.Next(0, 100));
                        }
                    }
                }
                Sleep(250);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Auto-potion error: {ex.Message}");
                Sleep(1000);
            }
        }
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
        if (threadFlags["recording"])
        {
        }
        if (threadFlags["playing"] && currentTarget != null)
        {
            int distanceX = Math.Abs(currentTarget.X - currentX);
            int distanceY = Math.Abs(currentTarget.Y - currentY);
            int barLength = 20;
            int progress = (int)Math.Round(
                (double)(currentCoordIndex + 1) / totalCoords * barLength
            );
            for (int i = 0; i < barLength; i++)
            {
            }
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
            case ConsoleKey.S:
                StopPositionAlertSound();
                Console.WriteLine("Position alert sound stopped manually.");
                break;
            case ConsoleKey.Q:
                if (SPAWNWATCHER.IsColorAlarmActive())
                {
                    SPAWNWATCHER.StopColorAlarm();
                    return;
                }
                programRunning = false;
                memoryReadActive = false;
                threadFlags["recording"] = false;
                threadFlags["playing"] = false;
                threadFlags["autopot"] = false;
                StopPositionAlertSound();
                break;
            case ConsoleKey.W:
                bool isActive = SPAWNWATCHER.Toggle(targetWindow, pixelSize);
                threadFlags["spawnwatch"] = isActive;
                Console.WriteLine($"Spawn watcher {(isActive ? "enabled" : "disabled")}");
                break;
            case ConsoleKey.F:
                ChangeOutfit(-1);
                break;
            case ConsoleKey.G:
                ChangeOutfit(1);
                break;
            case ConsoleKey.H:
                ChangeOutfit(-10);
                break;
            case ConsoleKey.J:
                ChangeOutfit(10);
                break;
            case ConsoleKey.O:
                threadFlags["overlay"] = !threadFlags["overlay"];
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
    private static DateTime lastF6Press = DateTime.MinValue;
    private static bool canPressF6 = true;
    private static readonly TimeSpan F6Cooldown = TimeSpan.FromSeconds(0.7);
    static void SendKeyPress(int key)
    {
        if (key == 0x75)
        {
            DateTime currentTime = DateTime.Now;
            if (!canPressF6 || (currentTime - lastF6Press) < F6Cooldown)
            {
                return;
            }
            lastF6Press = currentTime;
            canPressF6 = false;
            System.Threading.Timer cooldownTimer = null;
            cooldownTimer = new System.Threading.Timer((state) =>
            {
                canPressF6 = true;
                cooldownTimer?.Dispose();
            }, null, F6Cooldown, TimeSpan.Zero);
        }
        SendMessage(targetWindow, WM_KEYDOWN, key, IntPtr.Zero);
        Sleep(random.Next(10, 25));
        SendMessage(targetWindow, WM_KEYUP, key, IntPtr.Zero);
    }
    static void InstantSendKeyPress(int key)
    {
        SendMessage(targetWindow, WM_KEYDOWN, key, IntPtr.Zero);
        Sleep(random.Next(10, 25));
        SendMessage(targetWindow, WM_KEYUP, key, IntPtr.Zero);
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
        bool moveXFirst = Math.Abs(diffX) >= Math.Abs(diffY);
        if (moveXFirst)
        {
            if (diffX > 0)
            {
                SendKeyPress(VK_RIGHT);
            }
            else if (diffX < 0)
            {
                SendKeyPress(VK_LEFT);
            }
        }
        else
        {
            if (diffY > 0)
            {
                SendKeyPress(VK_DOWN);
            }
            else if (diffY < 0)
            {
                SendKeyPress(VK_UP);
            }
        }
    }
    static bool shouldClickAround = true;
    private static int previousTargetId = 0;
    static ChasePathTracker chaseTracker = new ChasePathTracker();
    static Coordinate? previousChasePosition = null;
    static bool isReturningFromChase = false;
    static private HashSet<int> clickedAroundTargets = new HashSet<int>();
    static private int lastTrackedTargetId = 0;
    static bool firstFound = true;
    private static DateTime lastWaypointClickTime = DateTime.MinValue;
    private static bool waypointStuckDetectionEnabled = true;
    private static TimeSpan waypointStuckTimeout = TimeSpan.FromSeconds(2);
    private static int consecutiveStuckCount = 0;
    private static int maxConsecutiveStuckCount = 3;
    private static Coordinate? lastClickedWaypoint = null;
    private static List<Point> failedClickPoints = new List<Point>();
    private static int waypointRetryCount = 0;
    private static readonly int MAX_WAYPOINT_RETRIES = 3;
    private static DateTime lastFailedClickPointsCleanupTime = DateTime.MinValue;
    private static readonly TimeSpan FAILED_CLICK_POINTS_CLEANUP_INTERVAL = TimeSpan.FromMinutes(5);
    static bool boolInit = true;
















    static void PlayCoordinates()
    {
        UpdateUIPositions();
        Console.WriteLine("Path playback starting...");

        string json = File.ReadAllText(cordsFilePath);
        loadedCoords = JsonSerializer.Deserialize<CoordinateData>(json);

        if (loadedCoords == null || loadedCoords.cords.Count == 0)
        {
            threadFlags["playing"] = false;
            return;
        }

        List<Coordinate> waypoints = loadedCoords.cords;
        totalCoords = waypoints.Count;

        // Find starting position
        int currentX, currentY, currentZ;
        lock (memoryLock)
        {
            currentX = posX;
            currentY = posY;
            currentZ = posZ;
        }

        // Use the improved FindClosestWaypointIndex
        if (boolInit)
        {
            currentCoordIndex = FindClosestWaypointIndex(waypoints, currentX, currentY, currentZ);
            boolInit = false;
        }
        else
        {
            currentCoordIndex = FindClosestWaypointIndex(waypoints, currentX, currentY, currentZ);

        }
            
        // Initialize or validate global state
        if (globalLastIndex < 0 || Math.Abs(globalLastIndex - currentCoordIndex) > maxBacktrackDistance)
        {
            globalLastIndex = currentCoordIndex;
            Console.WriteLine($"[DEBUG] Initialized global state. Starting at waypoint index: {currentCoordIndex}");
        }
        else
        {
            Console.WriteLine($"[DEBUG] Using existing global state. Current: {currentCoordIndex}, Global: {globalLastIndex}");
        }

        // Main navigation loop - rest of the code remains the same...
        while (threadFlags["playing"])
        {
            try
            {
                // Update current position
                lock (memoryLock)
                {
                    currentX = posX;
                    currentY = posY;
                    currentZ = posZ;
                }

                // Check if combat is active
                int currentTargetId;
                lock (memoryLock)
                {
                    currentTargetId = targetId;
                }

                if (currentTargetId == 0)
                {
                    SendKeyPress(VK_F6);
                    Thread.Sleep(150);
                }
                lock (memoryLock)
                {
                    currentTargetId = targetId;
                }

                // If combat is active, wait until target is killed
                if (currentTargetId != 0)
                {
                    Console.WriteLine($"[COMBAT] Fighting target {currentTargetId}...");

                    // Wait until target is killed
                    while (threadFlags["playing"])
                    {
                        lock (memoryLock)
                        {
                            if (targetId == 0)
                            {
                                Console.WriteLine("[COMBAT] Target killed!");
                                // Click around for loot after killing target
                                ClickAroundCharacter(targetWindow);
                                break;
                            }
                        }
                        Sleep(100);
                    }

                    // Update current position after combat
                    lock (memoryLock)
                    {
                        currentX = posX;
                        currentY = posY;
                        currentZ = posZ;
                    }

                    // Find closest waypoint to continue from using the improved function
                    currentCoordIndex = FindClosestWaypointIndex(waypoints, currentX, currentY, currentZ);
                    Console.WriteLine($"[COMBAT] Resuming from waypoint index {currentCoordIndex}");
                    continue;
                }

                // Get next navigation point
                NavigationAction action = DetermineNextAction(waypoints, currentX, currentY, currentZ);

                // Execute the action
                switch (action.Type)
                {
                    case ActionType.UseKeyboard:
                        MoveCharacterTowardsWaypoint(currentX, currentY, action.TargetX, action.TargetY);
                        currentCoordIndex = action.WaypointIndex;
                        globalLastIndex = currentCoordIndex; // Update global state
                        break;

                    case ActionType.ClickWaypoint:
                        if (ClickWaypoint(action.TargetX, action.TargetY))
                        {
                            WaitForMovementCompletion(action.TargetX, action.TargetY);
                        }
                        currentCoordIndex = action.WaypointIndex;
                        globalLastIndex = currentCoordIndex; // Update global state
                        break;

                    case ActionType.UseF4:
                        HandleF4Transition(action.TargetX, action.TargetY, action.FromZ, action.ToZ);
                        currentCoordIndex = action.WaypointIndex;
                        globalLastIndex = currentCoordIndex; // Update global state
                        break;

                    case ActionType.WaitForTarget:
                        SendKeyPress(VK_F6);
                        Sleep(100);
                        break;
                }

                Sleep(100);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in path playback: {ex.Message}");
                Sleep(1000);
            }
        }
    }

    // Determine what action to take next
    static NavigationAction DetermineNextAction(List<Coordinate> waypoints, int currentX, int currentY, int currentZ)
    {
        // Find the furthest reachable waypoint (max distance = 5)
        Coordinate target = FindFurthestReachableWaypoint(waypoints, currentX, currentY, currentZ);

        // Check if we need to change floors
        if (target.Z != currentZ)
        {
            Console.WriteLine($"[NAV] Detected floor change needed: Current Z={currentZ}, Target Z={target.Z}");
            return HandleZLevelChange(target, currentX, currentY, currentZ, waypoints);
        }

        // Check if we're already close enough to the target
        int distanceX = Math.Abs(target.X - currentX);
        int distanceY = Math.Abs(target.Y - currentY);

        if (distanceX == 0 && distanceY == 0)
        {
            Console.WriteLine($"[NAV] Already at target position - distance X={distanceX}, Y={distanceY}");
            return new NavigationAction
            {
                Type = ActionType.WaitAtPosition,
                TargetX = currentX,
                TargetY = currentY,
                WaypointIndex = currentCoordIndex
            };
        }

        // Decide whether to use keyboard or click
        int totalDistance = distanceX + distanceY;

        if (totalDistance <= 1)
        {
            // Close enough - use keyboard
            Console.WriteLine($"[NAV] Using keyboard - total distance {totalDistance}");
            return new NavigationAction
            {
                Type = ActionType.UseKeyboard,
                TargetX = target.X,
                TargetY = target.Y,
                WaypointIndex = FindWaypointIndex(waypoints, target)
            };
        }
        else
        {
            // Far enough - use click
            Console.WriteLine($"[NAV] Using click - total distance {totalDistance} > 3");
            return new NavigationAction
            {
                Type = ActionType.ClickWaypoint,
                TargetX = target.X,
                TargetY = target.Y,
                WaypointIndex = FindWaypointIndex(waypoints, target)
            };
        }
    }

    // Add these static variables to track progression
    static int lastTargetedIndex = -1; // Remember the last waypoint we actually targeted
    static Coordinate lastTargetedWaypoint = null;

    // Fixed FindFurthestReachableWaypoint with proper Z-level handling
    static Coordinate FindFurthestReachableWaypoint(List<Coordinate> waypoints, int currentX, int currentY, int currentZ)
    {
        const int MAX_DISTANCE = 5;
        Console.WriteLine($"[NAV] Looking for furthest reachable waypoint from ({currentX},{currentY},{currentZ})");
        Console.WriteLine($"[NAV] Current waypoint index: {currentCoordIndex}");
        Console.WriteLine($"[NAV] Global last index: {globalLastIndex}");

        // Sync currentCoordIndex with globalLastIndex if they're out of sync
        if (globalLastIndex >= 0 && Math.Abs(globalLastIndex - currentCoordIndex) <= maxBacktrackDistance)
        {
            if (globalLastIndex != currentCoordIndex)
            {
                Console.WriteLine($"[NAV] Syncing currentCoordIndex ({currentCoordIndex}) with globalLastIndex ({globalLastIndex})");
                currentCoordIndex = globalLastIndex;
            }
        }

        // Check if we've reached our last targeted waypoint
        if (lastTargetedIndex >= 0 && lastTargetedWaypoint != null)
        {
            int distanceToTarget = Math.Abs(lastTargetedWaypoint.X - currentX) + Math.Abs(lastTargetedWaypoint.Y - currentY);
            if (distanceToTarget <= 2) // Close enough to consider "reached"
            {
                Console.WriteLine($"[NAV] Reached targeted waypoint {lastTargetedIndex}. Advancing current index from {currentCoordIndex} to {lastTargetedIndex}");
                currentCoordIndex = lastTargetedIndex;
                globalLastIndex = lastTargetedIndex; // Update global state too
                lastTargetedIndex = -1; // Reset since we've reached it
                lastTargetedWaypoint = null;
            }
        }

        // Keep track of the best reachable waypoint found so far
        Coordinate furthest = null;
        int bestIndex = -1;
        int maxDistance = 0;

        // Look forward through the waypoint list (only forward, never backward)
        for (int i = 1; i < Math.Min(15, waypoints.Count); i++) // Start from i=1 to skip current waypoint
        {
            // Calculate next index, wrapping around if needed
            int checkIndex = (currentCoordIndex + i) % waypoints.Count;
            Coordinate check = waypoints[checkIndex];
            Console.WriteLine($"[NAV] Checking waypoint {checkIndex}: ({check.X},{check.Y},{check.Z})");

            // NEW LOGIC: Only consider Z-level changes if this is the very next waypoint (i == 1)
            if (check.Z != currentZ)
            {
                if (i == 1)
                {
                    // This is the next waypoint and it has a different Z level
                    Console.WriteLine($"[NAV] Next waypoint {checkIndex} requires floor change (Z={check.Z} vs current Z={currentZ})");

                    // Handle the floor change for the next waypoint
                    Coordinate transitionWaypoint = null;
                    int transitionIndex = -1;
                    int minTransitionDistance = int.MaxValue;

                    if (check.Z > currentZ)
                    {
                        // Need to go UP - find closest waypoint with higher Z
                        Console.WriteLine($"[NAV] Need to go UP to Z={check.Z}");
                        for (int j = 0; j < waypoints.Count; j++)
                        {
                            var candidate = waypoints[j];
                            if (candidate.Z == check.Z)
                            {
                                int distance = Math.Abs(candidate.X - currentX) + Math.Abs(candidate.Y - currentY);
                                if (distance < minTransitionDistance)
                                {
                                    minTransitionDistance = distance;
                                    transitionWaypoint = new Coordinate { X = candidate.X, Y = candidate.Y, Z = candidate.Z };
                                    transitionIndex = j;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Need to go DOWN - find closest waypoint with lower Z
                        Console.WriteLine($"[NAV] Need to go DOWN to Z={check.Z}");
                        for (int j = 0; j < waypoints.Count; j++)
                        {
                            var candidate = waypoints[j];
                            if (candidate.Z == check.Z)
                            {
                                int distance = Math.Abs(candidate.X - currentX) + Math.Abs(candidate.Y - currentY);
                                if (distance < minTransitionDistance)
                                {
                                    minTransitionDistance = distance;
                                    transitionWaypoint = new Coordinate { X = candidate.X, Y = candidate.Y, Z = candidate.Z };
                                    transitionIndex = j;
                                }
                            }
                        }
                    }

                    if (transitionWaypoint != null)
                    {
                        Console.WriteLine($"[NAV] Found transition waypoint: index {transitionIndex} at ({transitionWaypoint.X},{transitionWaypoint.Y},{transitionWaypoint.Z})");
                        lastTargetedIndex = checkIndex;
                        lastTargetedWaypoint = check;
                        return transitionWaypoint;
                    }
                    else
                    {
                        Console.WriteLine($"[NAV] No transition waypoint found - returning the next waypoint directly");
                        lastTargetedIndex = checkIndex;
                        lastTargetedWaypoint = check;
                        return check;
                    }
                }
                else
                {
                    // This is not the next waypoint and has different Z - skip it entirely
                    Console.WriteLine($"[NAV] Skipping waypoint {checkIndex} - different Z level (Z={check.Z}) and not next waypoint");
                    continue;
                }
            }

            // Calculate distance to this waypoint (same Z level as current)
            int distanceX = Math.Abs(check.X - currentX);
            int distanceY = Math.Abs(check.Y - currentY);
            int totalDistance = distanceX + distanceY;
            Console.WriteLine($"[NAV] Distance to waypoint {checkIndex}: dx={distanceX}, dy={distanceY}, total={totalDistance}");

            // Check if this waypoint is reachable
            if (distanceX <= MAX_DISTANCE && distanceY <= MAX_DISTANCE)
            {
                // This waypoint is reachable, update our best choice
                if (totalDistance > maxDistance || furthest == null)
                {
                    furthest = check;
                    bestIndex = checkIndex;
                    maxDistance = totalDistance;
                    Console.WriteLine($"[NAV] New best waypoint: {checkIndex} at distance {totalDistance}");
                }
            }
            else
            {
                // We've reached a waypoint that's too far, so we should stop
                Console.WriteLine($"[NAV] Waypoint {checkIndex} too far (dx={distanceX}, dy={distanceY}), stopping search");

                // If we found a reachable waypoint, break and return it
                if (furthest != null)
                {
                    Console.WriteLine($"[NAV] Returning previously found reachable waypoint {bestIndex}");
                    break;
                }
                else
                {
                    // No reachable waypoint found yet, continue to next iteration
                    Console.WriteLine($"[NAV] No reachable waypoint found yet, continuing search...");
                    continue;
                }
            }
        }

        // If we still haven't found any reachable waypoint, return a coordinate 1 square away in the direction of the next waypoint
        if (furthest == null)
        {
            Console.WriteLine($"[NAV] No reachable waypoint found, calculating direction to next waypoint");

            // Get the next waypoint in sequence
            int nextIndex = (currentCoordIndex + 1) % waypoints.Count;
            Coordinate nextWaypoint = waypoints[nextIndex];

            // Calculate direction to the next waypoint
            int deltaX = nextWaypoint.X - currentX;
            int deltaY = nextWaypoint.Y - currentY;

            int stepX = 0;
            int stepY = 0;

            bool canMoveX = deltaX != 0;
            bool canMoveY = deltaY != 0;

            Random rand = new Random();

            if (canMoveX && canMoveY)
            {
                // Randomly choose one axis to step in
                if (rand.Next(2) == 0)
                {
                    stepX = deltaX > 0 ? 1 : -1;
                }
                else
                {
                    stepY = deltaY > 0 ? 1 : -1;
                }
            }
            else if (canMoveX)
            {
                stepX = deltaX > 0 ? 1 : -1;
            }
            else if (canMoveY)
            {
                stepY = deltaY > 0 ? 1 : -1;
            }

            // Create coordinate 1 square away in the chosen direction
            furthest = new Coordinate
            {
                X = currentX + stepX,
                Y = currentY + stepY,
                Z = currentZ
            };

            bestIndex = -1;
            maxDistance = 1;

            Console.WriteLine($"[NAV] Returning directional step: ({furthest.X},{furthest.Y},{furthest.Z}) toward waypoint {nextIndex}");
        }

        // Remember what we're targeting
        if (bestIndex != currentCoordIndex)
        {
            lastTargetedIndex = bestIndex;
            lastTargetedWaypoint = furthest;
            Console.WriteLine($"[NAV] Targeting waypoint {bestIndex} at ({furthest.X},{furthest.Y},{furthest.Z})");
        }

        Console.WriteLine($"[NAV] Selected furthest reachable waypoint: {bestIndex} at ({furthest.X},{furthest.Y},{furthest.Z}) distance={maxDistance}");
        return furthest;
    }

    // Handle floor changes
    static NavigationAction HandleZLevelChange(Coordinate target, int currentX, int currentY, int currentZ, List<Coordinate> waypoints)
    {
        bool isGoingUp = target.Z < currentZ;
        HoleLocation? hole = GetHoleAtPosition(target.X, target.Y, currentZ);

        Console.WriteLine($"[FLOOR] Need to change floors from Z={currentZ} to Z={target.Z} ({(isGoingUp ? "UP" : "DOWN")})");
        if (hole != null)
        {
            Console.WriteLine($"[FLOOR] Found hole information: {hole}");
        }

        if (isGoingUp)
        {
            // Going UP - need to be at exact position and press F4
            int distanceX = Math.Abs(target.X - currentX);
            int distanceY = Math.Abs(target.Y - currentY);

            Console.WriteLine($"[FLOOR] Going UP - distance to F4 position: dx={distanceX}, dy={distanceY}");

            if (distanceX <= 1 && distanceY <= 1)
            {
                // We're at the F4 position
                Console.WriteLine($"[FLOOR] At F4 position - will press F4");
                return new NavigationAction
                {
                    Type = ActionType.UseF4,
                    TargetX = target.X,
                    TargetY = target.Y,
                    FromZ = currentZ,
                    ToZ = target.Z,
                    WaypointIndex = FindWaypointIndex(waypoints, target)
                };
            }
            else
            {
                // Move to F4 position
                Console.WriteLine($"[FLOOR] Moving to F4 position at ({target.X},{target.Y})");
                return new NavigationAction
                {
                    Type = ActionType.ClickWaypoint,
                    TargetX = target.X,
                    TargetY = target.Y,
                    FromZ = currentZ,
                    ToZ = target.Z,
                    WaypointIndex = currentCoordIndex
                };
            }
        }
        else
        {
            // Going DOWN - walk into the hole
            Console.WriteLine($"[FLOOR] Going DOWN - will click hole at ({target.X},{target.Y})");
            return new NavigationAction
            {
                Type = ActionType.ClickWaypoint,
                TargetX = target.X,
                TargetY = target.Y,
                FromZ = currentZ,
                ToZ = target.Z,
                WaypointIndex = FindWaypointIndex(waypoints, target)
            };
        }
    }

    // Handle F4 transitions
    static void HandleF4Transition(int targetX, int targetY, int fromZ, int toZ)
    {
        Console.WriteLine($"[F4] Pressing F4 to go from Z={fromZ} to Z={toZ}");

        SendKeyPress(VK_F4);

        // Wait for Z change
        DateTime startTime = DateTime.Now;
        while ((DateTime.Now - startTime).TotalMilliseconds < 2000)
        {
            Sleep(100);
            lock (memoryLock)
            {
                if (posZ == toZ)
                {
                    Console.WriteLine($"[F4] Successfully changed to Z={toZ}");
                    return;
                }
            }
        }

        Console.WriteLine($"[F4] Warning: Z change may have failed or taken longer than expected");
    }

    // Simplified click waypoint function
    static bool ClickWaypoint(int targetX, int targetY)
    {
        try
        {
            UpdateUIPositions();

            // Get window dimensions
            GetClientRect(targetWindow, out RECT rect);

            // Calculate center coordinates
            int centerX = (rect.Right - rect.Left) / 2 - 186;
            int centerY = (rect.Bottom - rect.Top) / 2 - baseYOffset;
            
            // Initial lParam calculation (this gets overwritten)
            int lParam = (centerX << 16) | (centerY & 0xFFFF);
           

            // Record center waypoint
            RecordWaypointClick(centerX, centerY);


            // Get current position
            int currentX, currentY;
            lock (memoryLock)
            {
                currentX = posX;
                currentY = posY;
            }
        

            // Calculate differences
            int diffX = targetX - currentX;
            int diffY = targetY - currentY;
          

            // Calculate screen coordinates
            int screenX = centerX + (diffX * pixelSize);
            int screenY = centerY + (diffY * pixelSize);
         
            // Create the lParam for the screen coordinates
            lParam = (screenY << 16) | (screenX & 0xFFFF);
          
            SendMessage(targetWindow, WM_MOUSEMOVE, IntPtr.Zero, lParam);
            Sleep(1);

            // Send the click
          
            SendMessage(targetWindow, WM_LBUTTONDOWN, 1, lParam);
            Sleep(1);

         
            SendMessage(targetWindow, WM_LBUTTONUP, IntPtr.Zero, lParam);

            // Record the click
            RecordWaypointClick(screenX, screenY);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLICK] Error: {ex.Message}");
            Console.WriteLine($"[CLICK] Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    // Wait for movement completion
    static void WaitForMovementCompletion(int targetX, int targetY)
    {
        DateTime startTime = DateTime.Now;
        const int TIMEOUT_MS = 5000;

        // Movement tracking variables
        int lastPosX = -1;
        int lastPosY = -1;
        int noMovementCount = 0;
        const int MAX_NO_MOVEMENT_COUNT = 1;

        while ((DateTime.Now - startTime).TotalMilliseconds < TIMEOUT_MS)
        {
            Sleep(500);
            SendKeyPress(VK_F6);

            lock (memoryLock)
            {
                // Check if monster appeared
                if (targetId != 0)
                {
                    return;
                }

                // Check if character moved
                if (lastPosX != -1 && lastPosY != -1)
                {
                    if (posX == lastPosX && posY == lastPosY)
                    {
                        noMovementCount++;
                        Console.WriteLine($"[MOVE] No movement detected. Count: {noMovementCount}/{MAX_NO_MOVEMENT_COUNT}");

                        if (noMovementCount >= MAX_NO_MOVEMENT_COUNT)
                        {
                            Console.WriteLine($"[MOVE] Character hasn't moved for {noMovementCount} checks. Returning early.");
                            return;
                        }
                    }
                    else
                    {
                        noMovementCount = 0; // Reset counter if character moved
                        Console.WriteLine($"[MOVE] Character moved from ({lastPosX},{lastPosY}) to ({posX},{posY})");
                    }
                }

                // Update last known position
                lastPosX = posX;
                lastPosY = posY;

                // Check if we've reached the target
                int distanceX = Math.Abs(targetX - posX);
                int distanceY = Math.Abs(targetY - posY);
                if (distanceX <= 1 && distanceY <= 1)
                {
                    Console.WriteLine($"[MOVE] Reached target position ({targetX},{targetY})");
                    Thread.Sleep(400);
                    return;
                }
            }
        }

        Console.WriteLine($"[MOVE] Timeout reached after {TIMEOUT_MS}ms");
    }

    // Helper function to find waypoint index
    static int FindWaypointIndex(List<Coordinate> waypoints, Coordinate target)
    {
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i].X == target.X && waypoints[i].Y == target.Y && waypoints[i].Z == target.Z)
                return i;
        }
        return currentCoordIndex;
    }

    // Navigation action types
    public enum ActionType
    {
        UseKeyboard,    // Walk with arrow keys
        ClickWaypoint,  // Click on game screen
        UseF4,          // Press F4 for stairs
        WaitAtPosition,
        WaitForTarget
    }

    // Navigation action structure
    public class NavigationAction
    {
        public ActionType Type { get; set; }
        public int TargetX { get; set; }
        public int TargetY { get; set; }
        public int FromZ { get; set; }
        public int ToZ { get; set; }
        public int WaypointIndex { get; set; }
    }
    static bool strictZCoordinateMode = true;

    static int globalLastIndex = -1; // Global state for waypoint progression
    static Coordinate globalLastPosition = null; // Last player position
    static DateTime lastPositionUpdate = DateTime.MinValue;
    static int maxBacktrackDistance = 15; // Max allowed backtrack distance

    // Modified FindClosestWaypointIndex with state preservation
    static int FindClosestWaypointIndex(
    List<Coordinate> waypoints,
    int currentX,
    int currentY,
    int currentZ
)
    {
        if (waypoints == null || waypoints.Count == 0)
        {
            return 0;
        }

        // Update global last position if we've moved
        bool positionChanged = false;
        if (globalLastPosition == null ||
            globalLastPosition.X != currentX ||
            globalLastPosition.Y != currentY ||
            globalLastPosition.Z != currentZ)
        {
            positionChanged = true;
            globalLastPosition = new Coordinate { X = currentX, Y = currentY, Z = currentZ };
            lastPositionUpdate = DateTime.Now;
        }

        // If we have a valid global last index and haven't moved much, prefer forward progression
        if (globalLastIndex >= 0 && globalLastIndex < waypoints.Count)
        {
            var lastWaypoint = waypoints[globalLastIndex];
            int distanceToLast = Math.Abs(lastWaypoint.X - currentX) + Math.Abs(lastWaypoint.Y - currentY);

            // If we're still close to our last waypoint and on the same Z level, don't backtrack
            if (distanceToLast <= 3 && lastWaypoint.Z == currentZ)
            {
                Console.WriteLine($"[WAYPOINT] Still close to last waypoint {globalLastIndex}, maintaining progression");
                return globalLastIndex;
            }
        }

        // Find closest waypoint with progression awareness
        int closestIndex = -1;
        int minDistance = int.MaxValue;

        // Calculate search range around current global last index
        int searchStart = Math.Max(0, globalLastIndex - maxBacktrackDistance);
        int searchEnd = Math.Min(waypoints.Count - 1, globalLastIndex + maxBacktrackDistance);

        // If no global last index set, search everything
        if (globalLastIndex < 0)
        {
            searchStart = 0;
            searchEnd = waypoints.Count - 1;
        }

        Console.WriteLine($"[WAYPOINT] Searching waypoints from {searchStart} to {searchEnd} (current global: {globalLastIndex})");

        // First pass: Look for waypoints on the same Z-level within the search range
        for (int i = searchStart; i <= searchEnd; i++)
        {
            var waypoint = waypoints[i];

            if (waypoint.Z != currentZ)
            {
                continue;
            }

            int distance = Math.Abs(waypoint.X - currentX) + Math.Abs(waypoint.Y - currentY);

            // Prefer forward progression if distances are similar
            if (globalLastIndex >= 0 && i > globalLastIndex && distance <= minDistance + 2)
            {
                minDistance = distance;
                closestIndex = i;
                Console.WriteLine($"[WAYPOINT] Preferring forward waypoint {i} (distance {distance})");
            }
            else if (distance < minDistance)
            {
                minDistance = distance;
                closestIndex = i;
            }
        }

        // Second pass: If no suitable waypoint found on same Z-level, expand search
        if (closestIndex == -1)
        {
            Console.WriteLine($"[WAYPOINT] No waypoint found on Z-level {currentZ}, expanding search");

            for (int i = searchStart; i <= searchEnd; i++)
            {
                var waypoint = waypoints[i];
                int distance = Math.Abs(waypoint.X - currentX) + Math.Abs(waypoint.Y - currentY);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestIndex = i;
                }
            }
        }

        // Third pass: If still nothing found, fall back to full search
        if (closestIndex == -1)
        {
            Console.WriteLine($"[WARNING] No waypoint found in range, doing full search");

            for (int i = 0; i < waypoints.Count; i++)
            {
                var waypoint = waypoints[i];

                if (waypoint.Z != currentZ)
                {
                    continue;
                }

                int distance = Math.Abs(waypoint.X - currentX) + Math.Abs(waypoint.Y - currentY);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestIndex = i;
                }
            }
        }

        // Final fallback
        if (closestIndex == -1)
        {
            Console.WriteLine("[WARNING] No waypoints found at all, returning index 0");
            closestIndex = 0;
        }

        // Update global state if we found a valid waypoint
        if (closestIndex >= 0)
        {
            // Only update global last index if we're moving forward or within reasonable backtrack distance
            if (globalLastIndex < 0 ||
                closestIndex > globalLastIndex ||
                Math.Abs(closestIndex - globalLastIndex) <= maxBacktrackDistance)
            {
                Console.WriteLine($"[WAYPOINT] Updating global last index from {globalLastIndex} to {closestIndex}");
                globalLastIndex = closestIndex;
            }
            else
            {
                Console.WriteLine($"[WAYPOINT] Not updating global index - would backtrack too far ({Math.Abs(closestIndex - globalLastIndex)} > {maxBacktrackDistance})");
            }
        }

        Console.WriteLine($"[WAYPOINT] Selected index {closestIndex} at ({waypoints[closestIndex].X}, {waypoints[closestIndex].Y}, {waypoints[closestIndex].Z}) distance {minDistance}");
        return closestIndex;
    }

    // Alternative version with optional strict Z filtering
    static int FindClosestWaypointIndex(
        List<Coordinate> waypoints,
        int currentX,
        int currentY,
        int currentZ,
        bool strictZFilter = true
    )
    {
        if (waypoints == null || waypoints.Count == 0)
        {
            return 0;
        }

        int closestIndex = -1;
        int minDistance = int.MaxValue;

        for (int i = 0; i < waypoints.Count; i++)
        {
            var waypoint = waypoints[i];

            // Apply Z-filter if strict mode is enabled
            if (strictZFilter && waypoint.Z != currentZ)
            {
                continue;
            }

            int distance = Math.Abs(waypoint.X - currentX) + Math.Abs(waypoint.Y - currentY);
            if (distance < minDistance)
            {
                minDistance = distance;
                closestIndex = i;
            }
        }

        // If no waypoint found with strict Z filtering, try without it
        if (closestIndex == -1 && strictZFilter)
        {
            Console.WriteLine($"[WARNING] No waypoint found on Z-level {currentZ}, trying without Z filter");
            return FindClosestWaypointIndex(waypoints, currentX, currentY, currentZ, false);
        }

        // If still no waypoint found, return 0 as last resort
        if (closestIndex == -1)
        {
            Console.WriteLine("[WARNING] No waypoints found at all, returning index 0");
            closestIndex = 0;
        }

        return closestIndex;
    }


























    static bool waypointRandomizationEnabled = false;
    static int randomizationRange = 1;
    static bool smallWindow = true;
    static bool previousSmallWindowValue = true;
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
    static int squareSizeBP;
    static (int, int)[] normalCoordinates;
    static (int, int)[] smallCoordinates;
    static (int, int)[] GetCorspeFoodCoordinates()
    {
        return smallWindow ? smallCoordinates : normalCoordinates;
    }
    static void UpdateUIPositions()
    {
        
        GetClientRect(targetWindow, out RECT windowRect);
        int windowHeight = windowRect.Bottom - windowRect.Top;
        smallWindow = windowHeight < 1200;

        squareSizeBP = smallWindow ? 40 : 45;
        bool valueChanged = (previousSmallWindowValue != smallWindow);
        previousSmallWindowValue = smallWindow;
        pixelSize = smallWindow ? 39 : 58;
        baseYOffset = smallWindow ? 225 : 300;
        inventoryX = smallWindow ? 620 : 940;
        inventoryY = 65;
        equipmentX = smallWindow ? 800 : 1115;
        equipmentY = 150;
        secondSlotBpX = smallWindow ? 840 : 1165;
        secondSLotBpY = 250;

        
        firstSlotBpX = smallWindow ? 840 - squareSizeBP : 1165 - squareSizeBP;
        firstSlotBpY = 250;
        closeCorpseX = smallWindow ? 944 : 1262;
        closeCorpseY = smallWindow ? 320 : 400;

        smallCoordinates = new (int, int)[]
        {
            (800, 460),
            (800+pixelSize, 460),
            (800+2*pixelSize, 460),
            (800+3*pixelSize, 460),
            (800, 500),
            (800+pixelSize, 500),
            (800+2*pixelSize, 500),
            (800+3*pixelSize, 500)
        };

        normalCoordinates = new (int, int)[]
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

        if (threadFlags["spawnwatch"] && SPAWNWATCHER.IsActive())
        {
            SPAWNWATCHER.UpdateScanArea(targetWindow, squareSizeBP);
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
    static int maxItemsToEat = 4;
    static void CorpseEatFood(IntPtr hWnd)
    {
        UpdateUIPositions();
        (int x, int y)[] locations = GetCorspeFoodCoordinates();
        GetClientRect(hWnd, out RECT rect);
        if (rect.Right < 1237 || rect.Bottom < 319)
        {
            int itemsToProcess = Math.Min(maxItemsToEat, locations.Length);
            for (int i = 0; i < itemsToProcess; i++)
            {
                int x = locations[i].x;
                int y = locations[i].y;
                POINT screenPoint = new POINT { X = x, Y = y };
                ClientToScreen(hWnd, ref screenPoint);
                Sleep(1);
                VirtualRightClick(hWnd, x, y);
                Sleep(50);
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
        return (y << 16) | (x & 0xFFFF);
    }
    static void ShuffleArray((int dx, int dy)[] array, Random random)
    {
        int n = array.Length;
        for (int i = n - 1; i > 0; i--)
        {
            int j = random.Next(0, i + 1);
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
            isClickAroundInProgress = true;
            int currentX, currentY, currentZ;
            int monsterX = 0, monsterY = 0, monsterZ = 0;
            lock (memoryLock)
            {
                currentX = posX;
                currentY = posY;
                currentZ = posZ;
                monsterX = lastKnownMonsterX;
                monsterY = lastKnownMonsterY;
                monsterZ = lastKnownMonsterZ;
            }
            GetClientRect(hWnd, out RECT rect);
            int screenCenterX = (rect.Right - rect.Left) / 2 - 186;
            int screenCenterY = (rect.Bottom - rect.Top) / 2 - baseYOffset;
            (int dx, int dy)[] clickPattern = new[]
            {
            (0, 0),
            (0, -1),
            (1, -1),
            (1, 0),
            (1, 1),
            (0, 1),
            (-1, 1),
            (-1, 0),
            (-1, -1)
        };
            (int dx, int dy) monsterRelativePos = (0, 0);
            if (monsterX != 0 || monsterY != 0)
            {
                int relX = monsterX - currentX;
                int relY = monsterY - currentY;
                if (relX != 0 || relY != 0)
                {
                    monsterRelativePos = (
                        Math.Max(-1, Math.Min(1, relX)),
                        Math.Max(-1, Math.Min(1, relY))
                    );
                }
            }
            List<(int dx, int dy)> orderedClickPattern = new List<(int dx, int dy)>();
            if (monsterRelativePos.dx != 0 || monsterRelativePos.dy != 0)
            {
                orderedClickPattern.Add(monsterRelativePos);
            }
            foreach (var pos in clickPattern)
            {
                if (pos != monsterRelativePos)
                {
                    orderedClickPattern.Add(pos);
                }
            }
            for (int i = 0; i < orderedClickPattern.Count; i++)
            {
            }
            Sleep(1);
            foreach (var direction in orderedClickPattern)
            {
                int dx = direction.dx;
                int dy = direction.dy;
                int clickX = screenCenterX + (dx * pixelSize);
                int clickY = screenCenterY + (dy * pixelSize);
                Sleep(1);
                VirtualRightClick(targetWindow, clickX, clickY);
                Sleep(1);
                int currentTargetId;
                lock (memoryLock)
                {
                    currentTargetId = targetId;
                }
                if (currentTargetId != 0)
                {
                    lastClickAroundTargetId = currentTargetId;
                    lastClickAroundCompleted = DateTime.Now;
                    isClickAroundInProgress = false;
                    return true;
                }
            }
            CorpseEatFood(targetWindow);
            Sleep(1);
            ClickSecondSlotInBackpack(hWnd);
            Sleep(1);
            if (typeof(Program).GetMethod("PlayClickCompletedSound") != null)
            {
                PlayClickCompletedSound();
            }
            lastClickAroundCompleted = DateTime.Now;
            isClickAroundInProgress = false;
            return false;
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
        SendMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, lParam);
        Sleep(1);
        SendMessage(hWnd, WM_LBUTTONDOWN, 1, lParam);
        Sleep(1);
        SendMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
        Sleep(1);
        RecordClickPosition(x, y, true);
    }
    static void VirtualRightClick(IntPtr hWnd, int x, int y)
    {
        int lParam = (y << 16) | (x & 0xFFFF);
        SendMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, lParam);
        SendMessage(hWnd, WM_RBUTTONDOWN, 1, lParam);
        SendMessage(hWnd, WM_RBUTTONUP, IntPtr.Zero, lParam);
        //RecordClickPosition(x, y, false);
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
                    monsterInstancePtr = BitConverter.ToInt32(buffer, 0);
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
        "Valkryie",
        "Dwarf Guard"
    };
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
                    monsterInstancePtr = BitConverter.ToInt32(buffer, 0);
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
        }
        if (!string.IsNullOrEmpty(monsterName) && monsterName != "" && !whitelistedMonsterNames.Contains(monsterName))
        {
            threadFlags["recording"] = false;
            threadFlags["playing"] = false;
            threadFlags["autopot"] = false;
            memoryReadActive = false;
            PlayEmergencyAlert(60);
            Environment.Exit(1);
        }
        return (monsterX, monsterY, monsterZ, monsterName);
    }
    static void PlayEmergencyAlert(int durationSeconds)
    {
        Thread alertThread = new Thread(() =>
        {
            DateTime endTime = DateTime.Now.AddSeconds(durationSeconds);
            try
            {
                while (DateTime.Now < endTime)
                {
                    Beep(2000, 300);
                    Thread.Sleep(100);
                    Beep(1800, 300);
                    Thread.Sleep(100);
                    Beep(2000, 300);
                    Thread.Sleep(500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in emergency alert: {ex.Message}");
            }
        });
        alertThread.IsBackground = false;
        alertThread.Start();
        alertThread.Join();
    }
    static bool withLifeRing = true;
    static bool onceLifeRing = true;
    static int lastRingEquippedTargetId = 0;
    static bool isRingCurrentlyEquipped = false;
    static List<string> blacklistedRingMonsters = new List<string> { "Poison Spider", };
    static readonly int MAX_MONSTER_DISTANCE = 4;
    static void ToggleRing(IntPtr hWnd, bool equip)
    {
        return;
        ScanRingContainersForMisplacedRings();
        try
        {
            int currentTargetId;
            int invisiblityCodeVar;
            Thread.Sleep(256);
            lock (memoryLock)
            {
                currentTargetId = targetId;
                invisiblityCodeVar = invisibilityCode;
            }
            bool isRingEquipped = invisiblityCodeVar == 2;
            if ((equip && isRingEquipped) || (!equip && !isRingEquipped))
            {
                return;
            }
            if (equip && isRingEquipped == false && withLifeRing && currentTargetId != 0)
            {
                int lifeRingX = inventoryX;
                int lifeRingY = inventoryY + 3 * pixelSize + 15;
                IntPtr lifeRingSourceLParam = MakeLParam(equipmentX, equipmentY);
                SendMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, lifeRingSourceLParam);
                Sleep(1);
                SendMessage(hWnd, WM_LBUTTONDOWN, IntPtr.Zero, lifeRingSourceLParam);
                Sleep(1);
                IntPtr lifeRingDestLParam = MakeLParam(lifeRingX, lifeRingY);
                SendMessage(hWnd, WM_MOUSEMOVE, new IntPtr(MK_LBUTTON), lifeRingDestLParam);
                Sleep(1);
                SendMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, lifeRingDestLParam);
                Sleep(1);
                Sleep(1);
            }
            Sleep(256);
            if (equip && currentTargetId != 0)
            {
                var (monsterX, monsterY, monsterZ, monsterName) = GetTargetMonsterInfo();
                if (!string.IsNullOrEmpty(monsterName) && blacklistedRingMonsters.Contains(monsterName))
                {
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
                if (totalDistance > MAX_MONSTER_DISTANCE)
                {
                    return;
                }
            }
            int sourceX = equip ? inventoryX : equipmentX;
            int sourceY = equip ? inventoryY : equipmentY;
            int destX = equip ? equipmentX : inventoryX;
            int destY = equip ? equipmentY : inventoryY;
            IntPtr sourceLParam = MakeLParam(sourceX, sourceY);
            SendMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, sourceLParam);
            Sleep(25);
            SendMessage(hWnd, WM_LBUTTONDOWN, IntPtr.Zero, sourceLParam);
            Sleep(25);
            IntPtr destLParam = MakeLParam(destX, destY);
            SendMessage(hWnd, WM_MOUSEMOVE, new IntPtr(MK_LBUTTON), destLParam);
            Sleep(25);
            SendMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, destLParam);
            Sleep(128);
            onceLifeRing = true;
            if (!equip && withLifeRing && onceLifeRing && currentTargetId == 0)
            {
                onceLifeRing = false;
                int lifeRingX = inventoryX;
                int lifeRingY = inventoryY + 3 * pixelSize + 15;
                double manaPercent = 0;
                lock (memoryLock)
                {
                    manaPercent = (curMana / maxMana) * 100;
                }
                if (manaPercent < 90)
                {
                    IntPtr lifeRingSourceLParam = MakeLParam(lifeRingX, lifeRingY);
                    SendMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, lifeRingSourceLParam);
                    Sleep(25);
                    SendMessage(hWnd, WM_LBUTTONDOWN, IntPtr.Zero, lifeRingSourceLParam);
                    Sleep(25);
                    RecordClickPosition(lifeRingX, lifeRingY, true);
                    IntPtr lifeRingDestLParam = MakeLParam(equipmentX, equipmentY);
                    SendMessage(hWnd, WM_MOUSEMOVE, new IntPtr(MK_LBUTTON), lifeRingDestLParam);
                    Sleep(25);
                    SendMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, lifeRingDestLParam);
                    Sleep(1);
                }
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
    static CancellationTokenSource? positionAlertCts = null;
    static readonly object soundLock = new object();
    static DateTime lastPositionAlertTime = DateTime.MinValue;
    static readonly int POSITION_ALERT_COOLDOWN_SEC = 60;
    static readonly int MAX_DISTANCE_SQMS = 50;
    [DllImport("kernel32.dll")]
    static extern bool Beep(int frequency, int duration);
    [DllImport("winmm.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);
    const uint SND_FILENAME = 0x00020000;
    const uint SND_ASYNC = 0x0001;
    const uint SND_NODEFAULT = 0x0002;
    const int CLICK_COMPLETED_FREQ = 800;
    const int CLICK_COMPLETED_DURATION = 200;
    const int POSITION_ALERT_FREQ = 1200;
    const int POSITION_ALERT_DURATION = 500;
    static void InitializeSounds()
    {
        try
        {
            string clickCompletedSoundPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "click_completed.wav"
            );
            string positionAlertSoundPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "position_alert.wav"
            );
            if (!File.Exists(clickCompletedSoundPath))
            {
            }
            else
            {
            }
            if (!File.Exists(positionAlertSoundPath))
            {
            }
            else
            {
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing sounds: {ex.Message}");
        }
    }
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
                bool success = PlaySound(clickCompletedSoundPath, IntPtr.Zero, SND_FILENAME | SND_ASYNC);
                if (success)
                {
                    return;
                }
            }
            Beep(CLICK_COMPLETED_FREQ, CLICK_COMPLETED_DURATION);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error playing click completed sound: {ex.Message}");
            try
            {
                Console.Beep(CLICK_COMPLETED_FREQ, CLICK_COMPLETED_DURATION);
            }
            catch
            {
                Console.WriteLine("All sound methods failed!");
            }
        }
    }
    static void StartPositionAlertSound(int maxDurationSeconds = 60)
    {
        lock (soundLock)
        {
            if (positionAlertCts != null ||
                (DateTime.Now - lastPositionAlertTime).TotalSeconds < POSITION_ALERT_COOLDOWN_SEC)
            {
                return;
            }
            lastPositionAlertTime = DateTime.Now;
            positionAlertCts = new CancellationTokenSource();
            var token = positionAlertCts.Token;
            string positionAlertSoundPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "position_alert.wav"
            );
            bool useWavFile = File.Exists(positionAlertSoundPath);
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
                                bool success = PlaySound(positionAlertSoundPath, IntPtr.Zero, SND_FILENAME | SND_ASYNC);
                                if (!success)
                                {
                                    Beep(POSITION_ALERT_FREQ, POSITION_ALERT_DURATION);
                                }
                            }
                            else
                            {
                                Beep(POSITION_ALERT_FREQ, POSITION_ALERT_DURATION);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error playing position alert sound: {ex.Message}");
                            try
                            {
                                Console.Beep(POSITION_ALERT_FREQ, POSITION_ALERT_DURATION);
                            }
                            catch
                            {
                                Console.WriteLine("All sound methods failed!");
                            }
                        }
                        Thread.Sleep(1500);
                    }
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
    static void StopPositionAlertSound()
    {
        lock (soundLock)
        {
            if (positionAlertCts != null)
            {
                positionAlertCts.Cancel();
                positionAlertCts = null;
            }
        }
    }
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
        int minDistance = int.MaxValue;
        foreach (var waypoint in waypoints)
        {
            int distance = Math.Abs(waypoint.X - currentX) + Math.Abs(waypoint.Y - currentY);
            if (distance < minDistance)
            {
                minDistance = distance;
            }
        }
        if (minDistance > MAX_DISTANCE_SQMS)
        {
            StartPositionAlertSound();
        }
        else
        {
        }
    }
    class ChasePathTracker
    {
        private bool isChasing = false;
        private Coordinate? chaseStartPosition = null;
        private List<Coordinate> chasePath = new List<Coordinate>();
        private int lastTargetId = 0;
        private bool needToReturnToStart = false;
        private object chaseLock = new object();
        private static List<Coordinate>? waypoints = null;
        private Dictionary<string, DateTime> messageTypeTimes = new Dictionary<string, DateTime>();
        private const double DISPLAY_COOLDOWN_SECONDS = 1.5;
        private const string MSG_CHASE_START = "CHASE_START";
        private const string MSG_TARGET_CHANGE = "TARGET_CHANGE";
        private const string MSG_DISTANCE_EXCEED = "DISTANCE_EXCEED";
        private const string MSG_DISTANCE_OK = "DISTANCE_OK";
        private const string MSG_WAYPOINT_INFO = "WAYPOINT_INFO";
        private const string MSG_NO_WAYPOINTS = "NO_WAYPOINTS";

        // Store the approximate index at chase start
        private int chaseStartWaypointIndex = -1;

        public void Update(int currentX, int currentY, int currentZ, int targetId)
        {
            lock (chaseLock)
            {
                if (!isChasing && targetId != 0)
                {
                    StartChase(currentX, currentY, currentZ, targetId);
                }
                else if (isChasing && targetId != 0)
                {
                    RecordPosition(currentX, currentY, currentZ);
                    if (targetId != lastTargetId)
                    {
                        string message = $"[DEBUG] Target changed during chase from {lastTargetId} to {targetId}";
                        DisplayWithCooldown(MSG_TARGET_CHANGE, message);
                        lastTargetId = targetId;
                    }
                }
                else if (isChasing && targetId == 0 && lastTargetId != 0)
                {
                    if (Program.loadedCoords != null && Program.loadedCoords.cords.Count > 0)
                    {
                        waypoints = Program.loadedCoords.cords;
                        EndChase(currentX, currentY, currentZ, waypoints);
                    }
                    else
                    {
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
            chasePath.Add(new Coordinate { X = x, Y = y, Z = z });

            // Store the approximate waypoint index when chase starts
            if (waypoints != null && waypoints.Count > 0)
            {
                chaseStartWaypointIndex = FindClosestWaypointIndex(waypoints, x, y, z);
            }
        }

        private void RecordPosition(int x, int y, int z)
        {
            if (chasePath.Count == 0 ||
                chasePath[chasePath.Count - 1].X != x ||
                chasePath[chasePath.Count - 1].Y != y ||
                chasePath[chasePath.Count - 1].Z != z)
            {
                chasePath.Add(new Coordinate { X = x, Y = y, Z = z });
                const int MAX_PATH_LENGTH = 1000;
                if (chasePath.Count > MAX_PATH_LENGTH)
                {
                    chasePath.RemoveAt(0);
                }
            }
        }

        private void EndChaseSimple(int currentX, int currentY, int currentZ)
        {
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
                ResetChaseState();
            }
        }

        private void EndChase(int currentX, int currentY, int currentZ, List<Coordinate> waypoints)
        {
            // First check if we're already close to ANY waypoint ahead of our starting position
            var nearbyWaypoint = FindNearestWaypointInDirection(waypoints, currentX, currentY, currentZ);

            if (nearbyWaypoint.found)
            {
                string message = $"[DEBUG] Found nearby waypoint ahead at index {nearbyWaypoint.index} (distance: {nearbyWaypoint.distance}), continuing path without returning to start";
                DisplayWithCooldown(MSG_WAYPOINT_INFO, message);

                // Update the current waypoint index to the found waypoint
                Program.currentCoordIndex = nearbyWaypoint.index;
                ResetChaseState();
                return;
            }

            // If no waypoint ahead is close enough, check if we're close to any waypoint at all
            int closestIndex = FindClosestWaypointIndex(waypoints, currentX, currentY, currentZ);
            int distanceX = Math.Abs(currentX - waypoints[closestIndex].X);
            int distanceY = Math.Abs(currentY - waypoints[closestIndex].Y);

            if (distanceX < 5 && distanceY < 5)
            {
                string message = $"[DEBUG] Already close to waypoint at index {closestIndex} (dx={distanceX}, dy={distanceY}), no need to return";
                DisplayWithCooldown(MSG_DISTANCE_OK, message);
                Program.currentCoordIndex = closestIndex;
                ResetChaseState();
                return;
            }

            // Only return to start if we're far from all waypoints
            string message2 = $"[DEBUG] Far from all waypoints (closest distance: {distanceX + distanceY}), need to return to start position";
            DisplayWithCooldown(MSG_DISTANCE_EXCEED, message2);
            needToReturnToStart = true;
        }

        // Find nearby waypoint that's ahead in the path
        private (bool found, int index, int distance) FindNearestWaypointInDirection(List<Coordinate> waypoints, int currentX, int currentY, int currentZ)
        {
            if (waypoints == null || waypoints.Count == 0 || chaseStartWaypointIndex == -1)
                return (false, -1, int.MaxValue);

            int bestIndex = -1;
            int bestDistance = int.MaxValue;
            const int MAX_DISTANCE = 8; // Slightly larger radius for checking

            // Check waypoints ahead of our starting position (prefer forward movement)
            for (int i = chaseStartWaypointIndex; i < waypoints.Count; i++)
            {
                if (waypoints[i].Z != currentZ)
                    continue;

                int distanceX = Math.Abs(waypoints[i].X - currentX);
                int distanceY = Math.Abs(waypoints[i].Y - currentY);
                int totalDistance = distanceX + distanceY;

                if (totalDistance <= MAX_DISTANCE && totalDistance < bestDistance)
                {
                    bestIndex = i;
                    bestDistance = totalDistance;
                }
            }

            if (bestIndex != -1)
            {
                return (true, bestIndex, bestDistance);
            }

            // If no forward waypoint found, check a few waypoints behind as well (but with higher preference for forward)
            int backwardStartIndex = Math.Max(0, chaseStartWaypointIndex - 2);
            for (int i = backwardStartIndex; i < chaseStartWaypointIndex; i++)
            {
                if (waypoints[i].Z != currentZ)
                    continue;

                int distanceX = Math.Abs(waypoints[i].X - currentX);
                int distanceY = Math.Abs(waypoints[i].Y - currentY);
                int totalDistance = distanceX + distanceY;

                if (totalDistance <= MAX_DISTANCE && totalDistance < bestDistance)
                {
                    bestIndex = i;
                    bestDistance = totalDistance;
                }
            }

            return bestIndex != -1 ? (true, bestIndex, bestDistance) : (false, -1, int.MaxValue);
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
            chaseStartWaypointIndex = -1;
        }

        public List<Coordinate> GetChasePath()
        {
            lock (chaseLock)
            {
                return new List<Coordinate>(chasePath);
            }
        }

        private int FindClosestWaypointIndex(List<Coordinate> waypoints, int currentX, int currentY, int currentZ)
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

        private void DisplayWithCooldown(string messageType, string message)
        {
            DateTime now = DateTime.Now;
            if (!messageTypeTimes.ContainsKey(messageType) ||
                (now - messageTypeTimes[messageType]).TotalSeconds >= DISPLAY_COOLDOWN_SECONDS)
            {
                messageTypeTimes[messageType] = now;
                Console.WriteLine(message);
            }
        }

        public void DisplayMessage(string messageType, string message)
        {
            lock (chaseLock)
            {
                DisplayWithCooldown(messageType, message);
            }
        }
    }
    static Dictionary<int, DateTime> blacklistedTargetTimers = new Dictionary<int, DateTime>();
    static DateTime lastPositionTime = DateTime.MinValue;
    static int lastPositionX = 0;
    static int lastPositionY = 0;
    static bool isPlayerStuck = false;
    static readonly TimeSpan TARGET_BLACKLIST_DURATION = TimeSpan.FromSeconds(30);
    static readonly int STUCK_DETECTION_TIME_MS = 2300;
    static bool IsTargetBlacklisted(int targetId)
    {
        if (!blacklistedTargetTimers.ContainsKey(targetId))
            return false;
        if (DateTime.Now > blacklistedTargetTimers[targetId])
        {
            blacklistedTargetTimers.Remove(targetId);
            return false;
        }
        return true;
    }
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
        }
    }
    static void UpdatePlayerMovementStatus(int currentX, int currentY)
    {
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
            TimeSpan stuckTime = DateTime.Now - lastPositionTime;
            isPlayerStuck = stuckTime.TotalMilliseconds >= STUCK_DETECTION_TIME_MS;
        }
    }
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
        UpdatePlayerMovementStatus(currentX, currentY);
        if (currentTargetId != 0 && isPlayerStuck)
        {
            var (monsterX, monsterY, monsterZ) = GetTargetMonsterCoordinates();
            int distanceX = Math.Abs(monsterX - currentX);
            int distanceY = Math.Abs(monsterY - currentY);
            int totalDistance = distanceX + distanceY;
            if (totalDistance > MAX_MONSTER_DISTANCE)
            {
                blacklistedTargetTimers[currentTargetId] = DateTime.Now.Add(TARGET_BLACKLIST_DURATION);
                InstantSendKeyPress(VK_F6);
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
        foreach (var kvp in blacklistedTargetTimers)
        {
            TimeSpan remainingTime = kvp.Value - now;
            if (remainingTime.TotalSeconds > 0)
            {
            }
        }
    }
    static class SPAWNWATCHER
    {
        private static string imageFolderPath = "images";
        private static bool watcherActive = false;
        private static Thread? watcherThread = null;
        private static object watcherLock = new object();
        private static double matchThreshold = 0.84;
        private static bool verboseDebug = false;
        private static long scanCount = 0;
        private static long totalScanTime = 0;
        private static DateTime lastDebugOutput = DateTime.MinValue;
        private static readonly TimeSpan debugOutputInterval = TimeSpan.FromSeconds(3);
        private static int scanCenterX = 0;
        private static int scanCenterY = 0;
        private static int scanWidth = 0;
        private static int scanHeight = 0;
        private static bool colorDetectionEnabled = true;
        private static DateTime lastColorAlarmTime = DateTime.MinValue;
        private static readonly TimeSpan colorAlarmCooldown = TimeSpan.FromSeconds(60);
        private static DateTime lastMatchTime = DateTime.MinValue;
        private static readonly TimeSpan matchCooldown = TimeSpan.FromSeconds(30);
        private static MCvScalar redLowerBound1 = new MCvScalar(0, 100, 100, 0);
        private static MCvScalar redUpperBound1 = new MCvScalar(10, 255, 255, 0);
        private static MCvScalar redLowerBound2 = new MCvScalar(160, 100, 100, 0);
        private static MCvScalar redUpperBound2 = new MCvScalar(180, 255, 255, 0);
        private static MCvScalar yellowLowerBound = new MCvScalar(20, 100, 100, 0);
        private static MCvScalar yellowUpperBound = new MCvScalar(40, 255, 255, 0);
        private static MCvScalar blueLowerBound = new MCvScalar(100, 100, 100, 0);
        private static MCvScalar blueUpperBound = new MCvScalar(130, 255, 255, 0);
        private class TemplateInfo
        {
            public required string FilePath { get; set; }
            public required Mat Template { get; set; }
            public string Name => Path.GetFileNameWithoutExtension(FilePath);
            public int Width => Template?.Width ?? 0;
            public int Height => Template?.Height ?? 0;
            public int Priority { get; set; } = 0;
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
                    return;
                }
                if (!Directory.Exists(imageFolderPath))
                {
                    Directory.CreateDirectory(imageFolderPath);
                }
                GetClientRect(gameWindow, out RECT rect);
                scanCenterX = (rect.Right - rect.Left) / 2 - 186;
                scanCenterY = (rect.Bottom - rect.Top) / 2 - 260;
                scanWidth = pixelSize * 10;
                scanHeight = pixelSize * 10;
                LoadTemplates();
                if (templates.Count == 0)
                {
                }
                else
                {
                    foreach (var template in templates)
                    {
                    }
                }
                scanCount = 0;
                totalScanTime = 0;
                watcherActive = true;
                watcherThread = new Thread(() => WatcherThreadFunction(gameWindow));
                watcherThread.IsBackground = true;
                watcherThread.Name = "OptimizedSpawnWatcher";
                watcherThread.Start();
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
                if (scanCount > 0)
                {
                    double avgScanTimeMs = (double)totalScanTime / scanCount;
                    double scansPerSecond = 1000.0 / avgScanTimeMs;
                }
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
                scanCenterY = (rect.Bottom - rect.Top) / 2 - 260;
                scanWidth = pixelSize * 10;
                scanHeight = pixelSize * 10;
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
                }
                else
                {
                    foreach (var template in templates)
                    {
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
                Dictionary<string, int> priorityMap = LoadTemplatePriorities();
                foreach (string filePath in imageFiles)
                {
                    try
                    {
                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        Mat template = CvInvoke.Imread(filePath, ImreadModes.Color);
                        if (template.IsEmpty)
                        {
                            continue;
                        }
                        int priority = 0;
                        if (priorityMap.ContainsKey(fileName))
                        {
                            priority = priorityMap[fileName];
                        }
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
                templates = templates.OrderByDescending(t => t.Priority).ToList();
                foreach (var template in templates)
                {
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SPAWN] Error loading templates: {ex.Message}");
            }
        }
        private static Dictionary<string, int> LoadTemplatePriorities()
        {
            Dictionary<string, int> priorities = new Dictionary<string, int>();
            string priorityFilePath = Path.Combine(imageFolderPath, "priorities.txt");
            if (!File.Exists(priorityFilePath))
            {
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
        private static unsafe Mat? CaptureWindowAsMat(IntPtr hWnd, int x, int y, int width, int height)
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
                        return null;
                }
                hdcMemDC = CreateCompatibleDC(hdcWindow);
                if (hdcMemDC == IntPtr.Zero)
                {
                    if (verboseDebug)
                        return null;
                }
                hBitmap = CreateCompatibleBitmap(hdcWindow, width, height);
                if (hBitmap == IntPtr.Zero)
                {
                    if (verboseDebug)
                        return null;
                }
                hOld = SelectObject(hdcMemDC, hBitmap);
                bool success = BitBlt(hdcMemDC, 0, 0, width, height, hdcWindow, x, y, SRCCOPY);
                if (!success)
                {
                    if (verboseDebug)
                        return null;
                }
                SelectObject(hdcMemDC, hOld);
                using (Bitmap bmp = Bitmap.FromHbitmap(hBitmap))
                {
                    Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                    BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                    try
                    {
                        result = new Mat(bmp.Height, bmp.Width, DepthType.Cv8U, 3, bmpData.Scan0, bmpData.Stride);
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
            try
            {
                Stopwatch iterationTimer = new Stopwatch();
                int iterationCount = 0;
                DateTime lastFullScanTime = DateTime.MinValue;
                const int TEMPLATE_MATCHING_INTERVAL_MS = 1000;
                Mat resultMat = new Mat();
                while (watcherActive)
                {
                    try
                    {
                        iterationTimer.Restart();
                        iterationCount++;
                        DateTime now = DateTime.Now;
                        GetClientRect(gameWindow, out RECT clientRect);
                        int windowWidth = clientRect.Right - clientRect.Left;
                        int windowHeight = clientRect.Bottom - clientRect.Top;
                        bool shouldDoFullScan = (now - lastFullScanTime).TotalMilliseconds >= TEMPLATE_MATCHING_INTERVAL_MS;
                        if (!shouldDoFullScan)
                        {
                            Thread.Sleep(100);
                            continue;
                        }
                        lastFullScanTime = now;
                        bool doTemplateMatching = templates.Count > 0 &&
                                                  (DateTime.Now - lastMatchTime) >= matchCooldown;
                        bool doColorDetection = colorChangeAlarmEnabled &&
                                                  (DateTime.Now - lastColorAlarmTime) >= colorAlarmCooldown;
                        if (!doTemplateMatching && !doColorDetection)
                        {
                            Thread.Sleep(100);
                            continue;
                        }
                        if (doTemplateMatching)
                        {
                            int scanLeft = Math.Max(0, scanCenterX - (scanWidth / 2));
                            int scanTop = Math.Max(0, scanCenterY - (scanHeight / 2));
                            if (verboseDebug && DateTime.Now.Subtract(lastDebugOutput) > debugOutputInterval)
                            {
                                lastDebugOutput = DateTime.Now;
                            }
                            using (Mat screenshot = CaptureWindowAsMat(gameWindow, scanLeft, scanTop, scanWidth, scanHeight))
                            {
                                if (screenshot == null)
                                {
                                    if (verboseDebug && iterationCount % 100 == 0)
                                    {
                                    }
                                }
                                else
                                {
                                    bool debugOutputThisIteration = verboseDebug && iterationCount % 50 == 0;
                                    if (debugOutputThisIteration)
                                    {
                                    }
                                    bool matchFound = false;
                                    foreach (var template in templates)
                                    {
                                        double minVal = 0, maxVal = 0;
                                        Point minLoc = new Point(), maxLoc = new Point();
                                        CvInvoke.MatchTemplate(screenshot, template.Template, resultMat, TemplateMatchingType.CcoeffNormed);
                                        CvInvoke.MinMaxLoc(resultMat, ref minVal, ref maxVal, ref minLoc, ref maxLoc);
                                        bool match = maxVal >= matchThreshold;
                                        if (debugOutputThisIteration || maxVal > 0.5)
                                        {
                                        }
                                        if (match)
                                        {
                                            Rectangle matchedRect = new Rectangle(maxLoc, new Size(template.Width, template.Height));
                                            Mat matchedRegion = new Mat(screenshot, matchedRect);
                                            var (isColorSimilar, colorSimilarityPercent) = IsColorSimilar(template.Template, matchedRegion);
                                            if (isColorSimilar)
                                            {
                                                HandleMatchFound(template.Name, screenshot, maxLoc, template.Width, template.Height, maxVal);
                                                lastMatchTime = DateTime.Now;
                                                matchFound = true;
                                                break;
                                            }
                                        }
                                    }
                                    if (iterationCount % 200 == 0 && !matchFound)
                                    {
                                    }
                                }
                            }
                        }
                        if (doColorDetection)
                        {
                            int colorDetectWidth = (int)(windowWidth * 0.1);
                            int colorDetectHeight = (int)(windowHeight * 0.1);
                            int colorDetectX = 0;
                            int colorDetectY = windowHeight - colorDetectHeight;
                            using (Mat bottomLeftCorner = CaptureWindowAsMat(gameWindow, colorDetectX, colorDetectY, colorDetectWidth, colorDetectHeight))
                            {
                                if (bottomLeftCorner != null)
                                {
                                    string debugDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug");
                                    if (!Directory.Exists(debugDir))
                                    {
                                        Directory.CreateDirectory(debugDir);
                                    }
                                    if (iterationCount % 10 == 0)
                                    {
                                        CvInvoke.Imwrite(Path.Combine(debugDir, "current_capture.png"), bottomLeftCorner);
                                    }
                                    string detectionResult = DetectColorChanges(bottomLeftCorner);
                                    if (detectionResult == "change")
                                    {
                                        Console.WriteLine("[COLOR] Significant color change detected in bottom-left corner!");
                                        HandleColorChangeDetection(bottomLeftCorner);
                                        lastColorAlarmTime = DateTime.Now;
                                    }
                                }
                            }
                        }
                        Thread.Sleep(200);
                    }
                    catch (Exception ex)
                    {
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
            MCvScalar templateMean = CvInvoke.Mean(template);
            MCvScalar matchedMean = CvInvoke.Mean(matchedRegion);
            double colorDistance = Math.Sqrt(
                Math.Pow(templateMean.V0 - matchedMean.V0, 2) +
                Math.Pow(templateMean.V1 - matchedMean.V1, 2) +
                Math.Pow(templateMean.V2 - matchedMean.V2, 2)
            );
            double maxPossibleDistance = Math.Sqrt(3 * Math.Pow(255, 2));
            double differencePercent = 100.0 * (colorDistance / maxPossibleDistance);
            double similarityPercent = 100.0 - differencePercent;
            bool isSimilar = differencePercent <= allowedDifferencePercent;
            return (isSimilar, similarityPercent);
        }
        private static void HandleMatchFound(string templateName, Mat screenshot, Point matchLocation, int templateWidth, int templateHeight, double similarity)
        {
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
                IntPtr gameWindow = Program.targetWindow;
                GetClientRect(gameWindow, out RECT clientRect);
                int windowWidth = clientRect.Right - clientRect.Left;
                int windowHeight = clientRect.Bottom - clientRect.Top;
                using (Mat fullScreenshot = CaptureWindowAsMat(gameWindow, 0, 0, windowWidth, windowHeight))
                {
                    if (fullScreenshot != null)
                    {
                        int scanLeft = Math.Max(0, scanCenterX - (scanWidth / 2));
                        int scanTop = Math.Max(0, scanCenterY - (scanHeight / 2));
                        Point fullScreenMatchLocation = new Point(
                            scanLeft + matchLocation.X,
                            scanTop + matchLocation.Y
                        );
                        Rectangle matchRect = new Rectangle(
                            fullScreenMatchLocation.X,
                            fullScreenMatchLocation.Y,
                            templateWidth,
                            templateHeight
                        );
                        CvInvoke.Rectangle(fullScreenshot, matchRect, new MCvScalar(0, 0, 255), 2);
                        string label = $"{templateName} ({similarity:F2})";
                        Point textLocation = new Point(matchRect.X, Math.Max(0, matchRect.Y - 10));
                        CvInvoke.PutText(fullScreenshot, label, textLocation, FontFace.HersheyComplex, 0.5, new MCvScalar(0, 0, 255), 2);
                        CvInvoke.Imwrite(matchPath, fullScreenshot);
                    }
                    else
                    {
                        CvInvoke.Imwrite(matchPath, screenshot);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SPAWN] Error saving match screenshot: {ex.Message}");
            }
            TriggerAlarm(templateName);
        }
        private static Mat? referenceImage = null;
        private static DateTime referenceImageTime = DateTime.MinValue;
        private static bool isFirstCapture = true;
        private static readonly TimeSpan referenceUpdateInterval = TimeSpan.FromMinutes(10);
        private static double colorDifferenceThreshold = 30.0;
        private static double changedPixelPercentageThreshold = 2.0;
        private static bool colorChangeAlarmEnabled = true;
        private static readonly object colorDetectionLock = new object();
        private static string DetectColorChanges(Mat currentImage)
        {
            try
            {
                string debugDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug");
                if (!Directory.Exists(debugDir))
                {
                    Directory.CreateDirectory(debugDir);
                }
                CvInvoke.Imwrite(Path.Combine(debugDir, "original_input.png"), currentImage);
                lock (colorDetectionLock)
                {
                    if (isFirstCapture || referenceImage == null)
                    {
                        if (referenceImage != null)
                        {
                            referenceImage.Dispose();
                        }
                        referenceImage = currentImage.Clone();
                        referenceImageTime = DateTime.Now;
                        isFirstCapture = false;
                        Console.WriteLine("[COLOR] First capture - saved as reference image");
                        CvInvoke.Imwrite(Path.Combine(debugDir, "reference_image.png"), referenceImage);
                        return null;
                    }
                    if (DateTime.Now - referenceImageTime > referenceUpdateInterval)
                    {
                        bool hasSignificantChanges = CheckForColorChanges(currentImage, referenceImage, debugDir, "reference_update_check");
                        if (!hasSignificantChanges)
                        {
                            Console.WriteLine("[COLOR] Updating reference image (scheduled update)");
                            referenceImage.Dispose();
                            referenceImage = currentImage.Clone();
                            referenceImageTime = DateTime.Now;
                            CvInvoke.Imwrite(Path.Combine(debugDir, "updated_reference_image.png"), referenceImage);
                        }
                        else
                        {
                            Console.WriteLine("[COLOR] Skipping reference update because significant changes detected");
                        }
                    }
                    bool changeDetected = CheckForColorChanges(currentImage, referenceImage, debugDir, "current_diff");
                    if (changeDetected)
                    {
                        Console.WriteLine("[COLOR] Significant color change detected!");
                        CvInvoke.Imwrite(Path.Combine(debugDir, "triggered_current.png"), currentImage);
                        return "change"; 
                    }
                }
                return null; 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[COLOR] Error in color change detection: {ex.Message}");
                return null;
            }
        }
        private static bool CheckForColorChanges(Mat currentImage, Mat referenceImage, string debugDir, string debugPrefix)
        {
            using (Mat diffImage = new Mat(currentImage.Size, DepthType.Cv8U, 3))
            {
                using (VectorOfMat currentChannels = new VectorOfMat())
                using (VectorOfMat referenceChannels = new VectorOfMat())
                {
                    CvInvoke.Split(currentImage, currentChannels);
                    CvInvoke.Split(referenceImage, referenceChannels);
                    using (Mat bDiff = new Mat())
                    using (Mat gDiff = new Mat())
                    using (Mat rDiff = new Mat())
                    using (Mat combinedMask = new Mat(currentImage.Rows, currentImage.Cols, DepthType.Cv8U, 1))
                    {
                        CvInvoke.AbsDiff(currentChannels[0], referenceChannels[0], bDiff);
                        CvInvoke.AbsDiff(currentChannels[1], referenceChannels[1], gDiff);
                        CvInvoke.AbsDiff(currentChannels[2], referenceChannels[2], rDiff);
                        CvInvoke.Imwrite(Path.Combine(debugDir, $"{debugPrefix}_b_diff.png"), bDiff);
                        CvInvoke.Imwrite(Path.Combine(debugDir, $"{debugPrefix}_g_diff.png"), gDiff);
                        CvInvoke.Imwrite(Path.Combine(debugDir, $"{debugPrefix}_r_diff.png"), rDiff);
                        using (Mat bMask = new Mat())
                        using (Mat gMask = new Mat())
                        using (Mat rMask = new Mat())
                        {
                            CvInvoke.Threshold(bDiff, bMask, colorDifferenceThreshold, 255, ThresholdType.Binary);
                            CvInvoke.Threshold(gDiff, gMask, colorDifferenceThreshold, 255, ThresholdType.Binary);
                            CvInvoke.Threshold(rDiff, rMask, colorDifferenceThreshold, 255, ThresholdType.Binary);
                            CvInvoke.BitwiseOr(bMask, gMask, combinedMask);
                            CvInvoke.BitwiseOr(combinedMask, rMask, combinedMask);
                            CvInvoke.Imwrite(Path.Combine(debugDir, $"{debugPrefix}_combined_mask.png"), combinedMask);
                            double totalPixels = combinedMask.Rows * combinedMask.Cols;
                            double changedPixels = CvInvoke.CountNonZero(combinedMask);
                            double percentChanged = (changedPixels / totalPixels) * 100.0;
                            currentImage.CopyTo(diffImage);
                            using (Mat redLayer = new Mat(diffImage.Size, DepthType.Cv8U, 1))
                            {
                                CvInvoke.BitwiseNot(combinedMask, redLayer);
                                using (VectorOfMat diffChannels = new VectorOfMat())
                                {
                                    CvInvoke.Split(diffImage, diffChannels);
                                    CvInvoke.BitwiseAnd(diffChannels[0], redLayer, diffChannels[0]);
                                    CvInvoke.BitwiseAnd(diffChannels[1], redLayer, diffChannels[1]);
                                    CvInvoke.BitwiseOr(diffChannels[2], combinedMask, diffChannels[2]);
                                    CvInvoke.Merge(diffChannels, diffImage);
                                }
                            }
                            CvInvoke.Imwrite(Path.Combine(debugDir, $"{debugPrefix}_visualization.png"), diffImage);
                            bool isSignificantChange = percentChanged >= changedPixelPercentageThreshold;
                            string percentText = $"Changed: {percentChanged:F1}% ({(isSignificantChange ? "ALERT" : "normal")})";
                            CvInvoke.PutText(
                                diffImage,
                                percentText,
                                new System.Drawing.Point(10, 20),
                                FontFace.HersheyComplex,
                                0.5,
                                isSignificantChange ? new MCvScalar(0, 0, 255) : new MCvScalar(0, 255, 0),
                                1
                            );
                            CvInvoke.Imwrite(Path.Combine(debugDir, $"{debugPrefix}_visualization_with_text.png"), diffImage);
                            return isSignificantChange;
                        }
                    }
                }
            }
        }
        private static void SaveColorDistributionInfoSimple(Mat image, string outputPath)
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(outputPath))
                {
                    writer.WriteLine("COLOR DETECTION INFORMATION");
                    writer.WriteLine("===========================");
                    writer.WriteLine($"Image size: {image.Width}x{image.Height}");
                    writer.WriteLine();
                    writer.WriteLine("COLOR DETECTION THRESHOLDS (HSV):");
                    writer.WriteLine("Cyan/Teal: H(75-95), S(50-255), V(50-255)");
                    writer.WriteLine("Green: H(45-75), S(50-255), V(50-255)");
                    writer.WriteLine("Yellow: H(15-45), S(50-255), V(50-255)");
                    writer.WriteLine("Red: H(0-10 or 160-180), S(50-255), V(50-255)");
                    writer.WriteLine("Blue: H(100-130), S(50-255), V(50-255)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[COLOR] Error saving color detection info: {ex.Message}");
            }
        }
        private static bool HasSignificantColor(Mat mask)
        {
            double nonZeroPixels = CvInvoke.CountNonZero(mask);
            double totalPixels = mask.Rows * mask.Cols;
            double percentage = (nonZeroPixels / totalPixels) * 100;
            bool isSignificant = percentage >= 2.0;
            return isSignificant;
        }
        private static void HandleColorChangeDetection(Mat image)
        {
            try
            {
                string matchesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "matches");
                if (!Directory.Exists(matchesDir))
                {
                    Directory.CreateDirectory(matchesDir);
                }
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string matchFileName = $"color_change_{timestamp}.png";
                string matchPath = Path.Combine(matchesDir, matchFileName);
                CvInvoke.Imwrite(matchPath, image);
                Console.WriteLine($"[COLOR] Color change detection screenshot saved to: {matchPath}");
                TriggerColorChangeAlert();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[COLOR] Error handling color change detection: {ex.Message}");
            }
        }
        public static CancellationTokenSource? colorAlarmCts = null;
        public static readonly object colorAlarmLock = new object();
        public static void TriggerColorChangeAlert()
        {
            try
            {
                Program.threadFlags["recording"] = false;
                Program.threadFlags["playing"] = false;
                Program.threadFlags["autopot"] = false;
                Program.threadFlags["spawnwatch"] = false;
                Program.memoryReadActive = false;
                Program.programRunning = false;
                IntPtr copyWindow = Program.targetWindow;
                lock (colorAlarmLock)
                {
                    colorAlarmCts = new CancellationTokenSource();
                }
                var token = colorAlarmCts.Token;
                Thread alarmSoundThread = new Thread(() =>
                {
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            Beep(1400, 200);
                            if (token.IsCancellationRequested) break;
                            Thread.Sleep(1);
                            Beep(1800, 200);
                            if (token.IsCancellationRequested) break;
                            Thread.Sleep(1);
                            Beep(1400, 200);
                            if (token.IsCancellationRequested) break;
                            Thread.Sleep(1);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[COLOR ALARM] Error in alarm sound: {ex.Message}");
                    }
                });
                alarmSoundThread.IsBackground = true;
                alarmSoundThread.Start();
                Thread inputMonitorThread = new Thread(() =>
                {
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            if (Console.KeyAvailable)
                            {
                                var key = Console.ReadKey(true).Key;
                                if (key == ConsoleKey.Q)
                                {
                                    Console.WriteLine("\n[COLOR ALARM] User pressed Q - stopping alarm...");
                                    StopColorAlarm();
                                    break;
                                }
                            }
                            Thread.Sleep(50);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[COLOR ALARM] Error in input monitor: {ex.Message}");
                    }
                });
                inputMonitorThread.IsBackground = true;
                inputMonitorThread.Start();
                Thread focusWindowThread = new Thread(() =>
                {
                    try
                    {
                        FocusGameWindow(copyWindow);
                        for (int i = 0; i < 600; i++) 
                        {
                            if (token.IsCancellationRequested)
                            {
                                break;
                            }
                            Thread.Sleep(100);
                        }
                        if (!token.IsCancellationRequested)
                        {
                            Console.WriteLine("[COLOR ALARM] Timeout reached - exiting program");
                            Environment.Exit(1);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[COLOR ALARM] Error in window focus: {ex.Message}");
                    }
                });
                focusWindowThread.IsBackground = false;
                focusWindowThread.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[COLOR ALARM] Error triggering color change alert: {ex.Message}");
            }
        }
        public static bool IsColorAlarmActive()
        {
            lock (colorAlarmLock)
            {
                return colorAlarmCts != null;
            }
        }
        public static void StopColorAlarm()
        {
            lock (colorAlarmLock)
            {
                if (colorAlarmCts != null)
                {
                    Console.WriteLine("[COLOR ALARM] Stopping color alarm...");
                    colorAlarmCts.Cancel();
                    colorAlarmCts.Dispose();
                    colorAlarmCts = null;
                    Program.threadFlags["recording"] = false;
                    Program.threadFlags["playing"] = false;
                    Program.threadFlags["autopot"] = false;
                    Program.threadFlags["spawnwatch"] = false;
                    Program.memoryReadActive = false;
                    Program.programRunning = false;
                    Console.WriteLine("[COLOR ALARM] Alarm stopped. Exiting program...");
                    Environment.Exit(0);
                }
            }
        }
        private static void TriggerAlarm(string templateName)
        {
            try
            {
                IntPtr copyWindow = Program.targetWindow;
                Program.threadFlags["recording"] = false;
                Program.threadFlags["playing"] = false;
                Program.threadFlags["autopot"] = false;
                Program.threadFlags["spawnwatch"] = false;
                Program.memoryReadActive = false;
                Program.programRunning = false;
                Program.StopPositionAlertSound();
                Thread alarmSoundThread = new Thread(() =>
                {
                    try
                    {
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
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ALARM] Error in alarm sound: {ex.Message}");
                    }
                });
                alarmSoundThread.IsBackground = true;
                alarmSoundThread.Start();
                Thread panicKeysThread = new Thread(() =>
                {
                    try
                    {
                        SendPanicKeysToGame(copyWindow);
                        Environment.Exit(1);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ALARM] Error in panic keys: {ex.Message}");
                        Environment.Exit(1);
                    }
                });
                panicKeysThread.IsBackground = false;
                panicKeysThread.Start();
                panicKeysThread.Join();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ALARM] Error triggering alarm: {ex.Message}");
                Environment.Exit(1);
            }
        }
        private static void FocusGameWindow(IntPtr gameWindow)
        {
            if (gameWindow == IntPtr.Zero)
            {
                return;
            }
            try
            {
                if (!SetForegroundWindow(gameWindow))
                {
                    int error = Marshal.GetLastWin32Error();
                    ShowWindow(gameWindow, SW_RESTORE);
                    Thread.Sleep(100);
                    SetForegroundWindow(gameWindow);
                    Thread.Sleep(100);
                }
                IntPtr activeWindow = GetForegroundWindow();
                if (activeWindow != gameWindow)
                {
                }
                else
                {
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
                return;
            }
            try
            {
                FocusGameWindow(gameWindow);
                Thread.Sleep(200);
                for (int i = 0; i < 3; i++)
                {
                    SendKeys.SendWait("{ESC}");
                    Thread.Sleep(100);
                }
                Random random = new Random();
                int questionMarkCount = random.Next(4, 8);
                for (int i = 0; i < questionMarkCount; i++)
                {
                    SendKeys.SendWait("?");
                    Thread.Sleep(50);
                }
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(50);
                for (int i = 0; i < questionMarkCount; i++)
                {
                    SendKeys.SendWait("?");
                    Thread.Sleep(50);
                }
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(50);
                DateTime endTime = DateTime.Now.AddSeconds(15);
                while (DateTime.Now < endTime)
                {
                    string[] directions = new string[] { "{UP}", "{RIGHT}", "{DOWN}", "{LEFT}" };
                    Random rand = new Random();
                    int movementsInCycle = rand.Next(100, 250);
                    for (int i = 0; i < movementsInCycle; i++)
                    {
                        string direction = directions[rand.Next(directions.Length)];
                        bool useCtrl = true;
                        if (useCtrl)
                        {
                            SendKeys.SendWait("^" + direction);
                        }
                        else
                        {
                            SendKeys.SendWait(direction);
                        }
                        int keyDelay = rand.Next(20, 30);
                        Thread.Sleep(keyDelay);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ALARM] Error sending panic keys: {ex.Message}");
            }
        }
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
        const int SW_RESTORE = 9;
        const uint SRCCOPY = 0x00CC0020;
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }
    }
    static System.Windows.Forms.Form? overlayForm = null;
    static List<(int x, int y, int size, DateTime time, string label)> activeHighlights = new List<(int x, int y, int size, DateTime time, string label)>();
    static System.Windows.Forms.Timer? cleanupTimer = null;
    static object highlightLock = new object();
    static bool showDebugCenter = true;
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x00000020;
    const int WS_EX_LAYERED = 0x00080000;
    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    static int statsOverlayRightOffset = 380;
    static int statsOverlayBottomOffset = 40;
    static float statsOverlaySizeScale = 0.92f;
    static float statsOverlayOpacity = 0.99f;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int HWND_TOPMOST = -1;
    const int SWP_NOMOVE = 0x0002;
    const int SWP_NOSIZE = 0x0001;
    const int SWP_NOACTIVATE = 0x0010;
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    // Add this with your other overlay configuration variables
    static int overlayTopOffset = 650;  // Distance from top of game window (change this!)
    static bool showOverlayDebugInfo = false; // Set to true to see position info

    // Modified EnsureOverlayExists function with configurable top anchor
    static void EnsureOverlayExists()
    {
        if (overlayForm != null && overlayForm.IsHandleCreated && !overlayForm.IsDisposed)
            return;

        overlayForm = null;
        Thread overlayThread = new Thread(() =>
        {
            try
            {
                System.Windows.Forms.Form threadForm = null;
                try
                {
                    threadForm = new System.Windows.Forms.Form();
                    threadForm.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
                    threadForm.ShowInTaskbar = false;
                    threadForm.TopMost = true;
                    threadForm.Opacity = statsOverlayOpacity;
                    threadForm.TransparencyKey = Color.Black;
                    threadForm.BackColor = Color.Black;

                    // Prevent form from taking focus
                    threadForm.Deactivate += (s, e) =>
                    {
                        if (threadForm != null && !threadForm.IsDisposed)
                        {
                            threadForm.TopMost = false;
                            threadForm.TopMost = true;
                        }
                    };

                    // Set initial size
                    int baseWidth = 300;
                    int initialContentLines = 14; // Base content lines
                    int lineHeight = 18;
                    int bottomMargin = 20;
                    int baseHeight = initialContentLines * lineHeight + bottomMargin;

                    threadForm.Width = (int)(baseWidth * statsOverlaySizeScale);
                    threadForm.Height = (int)(baseHeight * statsOverlaySizeScale);
                    threadForm.StartPosition = System.Windows.Forms.FormStartPosition.Manual;

                    // Calculate initial position with configurable top offset
                    try
                    {
                        GetWindowRect(targetWindow, out RECT gameWindowRect);
                        int overlayX = Math.Max(0, gameWindowRect.Right - threadForm.Width - statsOverlayRightOffset);
                        int overlayY = gameWindowRect.Top + overlayTopOffset; // Uses configurable offset

                        threadForm.Location = new Point(overlayX, overlayY);

                        if (showOverlayDebugInfo)
                        {
                            Console.WriteLine($"[OVERLAY] Initial position: X={overlayX}, Y={overlayY}");
                            Console.WriteLine($"[OVERLAY] Top offset: {overlayTopOffset}");
                            Console.WriteLine($"[OVERLAY] Game window: Top={gameWindowRect.Top}, Bottom={gameWindowRect.Bottom}");
                        }
                    }
                    catch
                    {
                        // Fallback to screen position
                        threadForm.Location = new Point(
                            Math.Max(0, Screen.PrimaryScreen.WorkingArea.Right - threadForm.Width - statsOverlayRightOffset),
                            overlayTopOffset);
                    }

                    // Set window styles ONCE during creation
                    int exStyle = GetWindowLong(threadForm.Handle, GWL_EXSTYLE);
                    exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                    SetWindowLong(threadForm.Handle, GWL_EXSTYLE, exStyle);

                    // Cache target ID to minimize locking
                    int lastKnownTargetId = -1;
                    bool needsRedraw = false;

                    threadForm.Paint += (sender, e) =>
                    {
                        try
                        {
                            if (statsOverlaySizeScale != 1.0f)
                            {
                                e.Graphics.ScaleTransform(statsOverlaySizeScale, statsOverlaySizeScale);
                            }

                            // Create dark background
                            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                            {
                                float scaledWidth = threadForm.Width / statsOverlaySizeScale;
                                float scaledHeight = threadForm.Height / statsOverlaySizeScale;
                                e.Graphics.FillRectangle(bgBrush, 0, 0, scaledWidth, scaledHeight);
                            }

                            // Font setup
                            float titleFontSize = 10.0f;
                            float statsFontSize = 9.0f;
                            float smallFontSize = 8.0f;

                            using (Font titleFont = new Font("Arial", titleFontSize, FontStyle.Bold))
                            using (Font statsFont = new Font("Arial", statsFontSize, FontStyle.Regular))
                            using (Font smallFont = new Font("Arial", smallFontSize, FontStyle.Regular))
                            using (Brush whiteBrush = new SolidBrush(Color.White))
                            using (Brush greenBrush = new SolidBrush(Color.LightGreen))
                            using (Brush blueBrush = new SolidBrush(Color.LightBlue))
                            using (Brush yellowBrush = new SolidBrush(Color.Yellow))
                            using (Brush orangeBrush = new SolidBrush(Color.Orange))
                            using (Brush pinkBrush = new SolidBrush(Color.LightPink))
                            using (Brush redBrush = new SolidBrush(Color.LightCoral))
                            using (Brush grayBrush = new SolidBrush(Color.LightGray))
                            {
                                float scaledWidth = threadForm.Width / statsOverlaySizeScale;
                                int y = 5;
                                int rightPadding = 20;
                                int lineSpacing = 18;

                                // Get data once with minimal lock time
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

                                // Show debug info if enabled
                                if (showOverlayDebugInfo)
                                {
                                    DrawRightAlignedStat(e, "Debug - Top Offset:", $"{overlayTopOffset}px",
                                        smallFont, grayBrush, scaledWidth - rightPadding, ref y, lineSpacing);
                                    DrawRightAlignedStat(e, "Debug - Form Y:", $"{threadForm.Top}",
                                        smallFont, grayBrush, scaledWidth - rightPadding, ref y, lineSpacing);
                                    y += 5;
                                }

                                // Draw title
                                string titleText = "RealeraDX - Live Stats";
                                SizeF titleSize = e.Graphics.MeasureString(titleText, titleFont);
                                e.Graphics.DrawString(titleText, titleFont, whiteBrush,
                                    scaledWidth - titleSize.Width - rightPadding, y);
                                y += lineSpacing + 2;

                                // Draw HP/Mana
                                DrawRightAlignedStat(e, "HP:", $"{curHP:F0}/{maxHP:F0} ({hpPercent:F1}%)",
                                    statsFont, hpPercent < 50 ? orangeBrush : greenBrush,
                                    scaledWidth - rightPadding, ref y, lineSpacing);

                                DrawRightAlignedStat(e, "Mana:", $"{curMana:F0}/{maxMana:F0} ({manaPercent:F1}%)",
                                    statsFont, blueBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                // Draw position
                                DrawRightAlignedStat(e, "Position:", $"({currentX}, {currentY}, {currentZ})",
                                    statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                // Draw invisibility and outfit
                                DrawRightAlignedStat(e, "Invisibility:", $"{currentInvisibilityCode} " +
                                    (currentInvisibilityCode == 2 ? "(Ring ON)" : "(Ring OFF)"),
                                    statsFont, pinkBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                DrawRightAlignedStat(e, "Outfit:", $"{currentOutfitValue}",
                                    statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                y += 5;

                                // Target info
                                if (currentTargetId != 0)
                                {
                                    var (monsterX, monsterY, monsterZ, monsterName) = GetTargetMonsterInfo();

                                    string targetHeader = "Target Info:";
                                    SizeF headerSize = e.Graphics.MeasureString(targetHeader, statsFont);
                                    e.Graphics.DrawString(targetHeader, statsFont, redBrush,
                                        scaledWidth - headerSize.Width - rightPadding, y);
                                    y += lineSpacing;

                                    DrawRightAlignedStat(e, "ID:", $"{currentTargetId}",
                                        statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                    if (!string.IsNullOrEmpty(monsterName))
                                    {
                                        DrawRightAlignedStat(e, "Name:", $"{monsterName}",
                                            statsFont, redBrush, scaledWidth - rightPadding, ref y, lineSpacing);
                                    }

                                    if (monsterX != 0 || monsterY != 0 || monsterZ != 0)
                                    {
                                        DrawRightAlignedStat(e, "Pos:", $"({monsterX}, {monsterY}, {monsterZ})",
                                            statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);

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

                                y += 5;

                                // Active features
                                string featuresText = "Active Features:";
                                SizeF featuresSize = e.Graphics.MeasureString(featuresText, statsFont);
                                e.Graphics.DrawString(featuresText, statsFont, yellowBrush,
                                    scaledWidth - featuresSize.Width - rightPadding, y);
                                y += lineSpacing;

                                DrawRightAlignedFeature(e, "Auto-Potions:", threadFlags["autopot"],
                                    statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);
                                DrawRightAlignedFeature(e, "Recording:", threadFlags["recording"],
                                    statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);
                                DrawRightAlignedFeature(e, "Playback:", threadFlags["playing"],
                                    statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);
                                DrawRightAlignedFeature(e, "Click Overlay:", clickOverlayActive,
                                    statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                // Playback progress
                                if (threadFlags["playing"] && currentTarget != null)
                                {
                                    y += 2;
                                    int distanceX = Math.Abs(currentTarget.X - currentX);
                                    int distanceY = Math.Abs(currentTarget.Y - currentY);

                                    DrawRightAlignedStat(e, "Waypoint:", $"{distanceX + distanceY} steps",
                                        statsFont, whiteBrush, scaledWidth - rightPadding, ref y, lineSpacing);

                                    float progress = (float)(currentCoordIndex + 1) / totalCoords;
                                    string progressText = $"Path: {(progress * 100):F1}% ({currentCoordIndex + 1}/{totalCoords})";
                                    SizeF progressSize = e.Graphics.MeasureString(progressText, statsFont);
                                    e.Graphics.DrawString(progressText, statsFont, whiteBrush,
                                        scaledWidth - progressSize.Width - rightPadding, y);
                                    y += lineSpacing;

                                    // Progress bar
                                    int progressWidth = (int)(scaledWidth - 40 - rightPadding);
                                    int barHeight = 6;
                                    int barX = 20;
                                    int filledWidth = (int)(progressWidth * progress);

                                    e.Graphics.FillRectangle(new SolidBrush(Color.DarkGray), barX, y, progressWidth, barHeight);
                                    e.Graphics.FillRectangle(new SolidBrush(Color.LimeGreen), barX, y, filledWidth, barHeight);
                                    y += barHeight + 2;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[OVERLAY] Paint error: {ex.Message}");
                        }
                    };

                    // Optimized update timer with reduced interval
                    System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer { Interval = 50 };

                    DateTime lastPositionUpdate = DateTime.MinValue;
                    DateTime lastStyleCheck = DateTime.MinValue;
                    DateTime lastRegularRefresh = DateTime.MinValue;
                    const int REGULAR_REFRESH_INTERVAL = 500; // Half a second

                    updateTimer.Tick += (sender, e) =>
                    {
                        try
                        {
                            DateTime now = DateTime.Now;
                            bool shouldUpdatePosition = (now - lastPositionUpdate).TotalMilliseconds >= 200;
                            bool shouldCheckStyle = (now - lastStyleCheck).TotalMilliseconds >= 1000;
                            bool shouldRegularRefresh = (now - lastRegularRefresh).TotalMilliseconds >= REGULAR_REFRESH_INTERVAL;

                            // Check target ID change (do this every tick for responsiveness)
                            int currentTargetId;
                            lock (memoryLock)
                            {
                                currentTargetId = targetId;
                            }

                            if (currentTargetId != lastKnownTargetId)
                            {
                                lastKnownTargetId = currentTargetId;
                                needsRedraw = true;
                                // Force immediate refresh on target change
                                threadForm.Invalidate();
                            }

                            // Regular refresh every 500ms (for position updates, etc.)
                            if (shouldRegularRefresh)
                            {
                                lastRegularRefresh = now;
                                needsRedraw = true;
                                if (showOverlayDebugInfo)
                                {
                                    Console.WriteLine($"[OVERLAY] Regular refresh triggered at {now:HH:mm:ss.fff}");
                                }
                            }

                            // Update position and size (less frequently)
                            if (shouldUpdatePosition)
                            {
                                lastPositionUpdate = now;

                                GetWindowRect(targetWindow, out RECT currentGameRect);
                                if (currentGameRect.Right > currentGameRect.Left &&
                                    currentGameRect.Bottom > currentGameRect.Top)
                                {
                                    // Calculate new size based on content
                                    int contentLines = 14; // Base lines
                                    if (showOverlayDebugInfo)
                                        contentLines += 2; // Debug info lines
                                    if (threadFlags["playing"] && currentTarget != null)
                                        contentLines += 3;
                                    if (lastKnownTargetId != 0)
                                        contentLines += 4;

                                    int lineHeight = 18;
                                    int bottomMargin = 20;
                                    int newHeight = (int)((contentLines * lineHeight + bottomMargin) * statsOverlaySizeScale);

                                    int baseWidth = 300;
                                    int newWidth = (int)(baseWidth * statsOverlaySizeScale);

                                    // ALWAYS calculate X position from the right edge
                                    int newX = Math.Max(0, currentGameRect.Right - newWidth - statsOverlayRightOffset);

                                    // USE CONFIGURABLE TOP OFFSET
                                    int newY = currentGameRect.Top + overlayTopOffset;

                                    // Only update if position/size actually changed
                                    if (newX != threadForm.Left || newY != threadForm.Top ||
                                        newWidth != threadForm.Width || newHeight != threadForm.Height)
                                    {
                                        if (showOverlayDebugInfo)
                                        {
                                            Console.WriteLine($"[OVERLAY] Position update: X={newX}, Y={newY}, Width={newWidth}, Height={newHeight}");
                                        }

                                        threadForm.Location = new Point(newX, newY);
                                        threadForm.Size = new Size(newWidth, newHeight);
                                        needsRedraw = true;
                                    }
                                }
                            }

                            // Check window styles less frequently
                            if (shouldCheckStyle && threadForm.IsHandleCreated)
                            {
                                lastStyleCheck = now;

                                // Only check styles if we haven't been destroyed/recreated
                                int currentExStyle = GetWindowLong(threadForm.Handle, GWL_EXSTYLE);
                                int expectedStyle = WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;

                                if ((currentExStyle & expectedStyle) != expectedStyle)
                                {
                                    SetWindowLong(threadForm.Handle, GWL_EXSTYLE, currentExStyle | expectedStyle);
                                }

                                // Ensure topmost less frequently
                                if (!threadForm.TopMost)
                                {
                                    threadForm.TopMost = true;
                                }
                            }

                            // Invalidate if needed
                            if (needsRedraw)
                            {
                                needsRedraw = false;
                                threadForm.Invalidate();
                            }

                            // Check if overlay should be closed
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

                    overlayForm = threadForm;
                    updateTimer.Start();
                    threadForm.Show();

                    // Main event loop with optimized timing
                    DateTime lastCheckTime = DateTime.MinValue;
                    const int EVENT_LOOP_SLEEP = 10;

                    while (!threadForm.IsDisposed && programRunning && memoryReadActive &&
                           threadFlags["overlay"] && overlayForm == threadForm)
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(EVENT_LOOP_SLEEP);

                        DateTime now = DateTime.Now;
                        if ((now - lastCheckTime).TotalSeconds >= 1)
                        {
                            lastCheckTime = now;

                            // Ensure we're still topmost
                            if (!threadForm.TopMost)
                            {
                                threadForm.TopMost = true;
                            }

                            // Final check for exit conditions
                            if (!programRunning || !memoryReadActive || !threadFlags["overlay"] ||
                                overlayForm != threadForm)
                            {
                                break;
                            }
                        }
                    }

                    updateTimer.Stop();
                    updateTimer.Dispose();

                    if (overlayForm == threadForm)
                    {
                        overlayForm = null;
                    }

                    if (!threadForm.IsDisposed)
                    {
                        threadForm.Close();
                        threadForm.Dispose();
                    }
                }
                finally
                {
                    if (threadForm != null && !threadForm.IsDisposed)
                    {
                        try
                        {
                            threadForm.Close();
                            threadForm.Dispose();
                        }
                        catch { }
                    }

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

        overlayThread.IsBackground = true;
        overlayThread.SetApartmentState(ApartmentState.STA);
        overlayThread.Start();
        Sleep(100);
    }

    // Helper function to change overlay position while running
    static void SetOverlayTopOffset(int newOffset)
    {
        if (newOffset >= 0 && newOffset <= 1000) // Reasonable range
        {
            overlayTopOffset = newOffset;
            Console.WriteLine($"[OVERLAY] Top offset changed to {overlayTopOffset}px");

            // Force immediate position update if overlay is active
            if (overlayForm != null && !overlayForm.IsDisposed)
            {
                overlayForm.Invalidate();
            }
        }
        else
        {
            Console.WriteLine($"[OVERLAY] Invalid offset: {newOffset}. Must be between 0 and 1000.");
        }
    }
    static void DrawRightAlignedStat(PaintEventArgs e, string label, string value, Font font,
       Brush brush, float rightEdge, ref int y, int lineSpacing)
    {
        string fullText = $"{label} {value}";
        SizeF textSize = e.Graphics.MeasureString(fullText, font);
        e.Graphics.DrawString(fullText, font, brush, rightEdge - textSize.Width, y);
        y += lineSpacing;
    }
    static void DrawRightAlignedFeature(PaintEventArgs e, string label, bool enabled, Font font,
        Brush brush, float rightEdge, ref int y, int lineSpacing)
    {
        string status = enabled ? "✅" : "❌";
        string fullText = $"{label} {status}";
        SizeF textSize = e.Graphics.MeasureString(fullText, font);
        e.Graphics.DrawString(fullText, font, brush, rightEdge - textSize.Width, y);
        y += lineSpacing;
    }
    class ClickHighlight
    {
        public int GameX { get; set; }
        public int GameY { get; set; }
        public int Size { get; set; }
        public DateTime ExpireTime { get; set; }
        public required string Label { get; set; }
        public float GetRemainingTimePercentage()
        {
            double msRemaining = (ExpireTime - DateTime.Now).TotalMilliseconds;
            return Math.Max(0f, Math.Min(1f, (float)(msRemaining / 1000.0)));
        }
        public bool IsExpired()
        {
            return DateTime.Now >= ExpireTime;
        }
    }
    static void StartOverlay()
    {
        if (overlayForm != null && !overlayForm.IsDisposed)
            return;
        EnsureOverlayExists();
    }
    static void StopOverlay()
    {
        if (overlayForm == null || overlayForm.IsDisposed)
            return;
        var form = overlayForm;
        overlayForm = null;
        try
        {
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
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OVERLAY] Error closing overlay: {ex.Message}");
        }
    }
    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
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
    [DllImport("gdi32.dll")]
    static extern IntPtr CreateSolidBrush(uint colorRef);
    [DllImport("gdi32.dll")]
    static extern uint RGB(int r, int g, int b);
    [DllImport("gdi32.dll")]
    static extern IntPtr GetStockObject(int fnObject);
    [DllImport("user32.dll")]
    static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
    const int HOLLOW_BRUSH = 5;
    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);
    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll", EntryPoint = "RGB")]
    static extern uint RGB(byte r, byte g, byte b);
    static uint MakeRGB(byte r, byte g, byte b)
    {
        return r | (((uint)g) << 8) | (((uint)b) << 16);
    }
    static object clickOverlayLock = new object();
    static List<(int x, int y, Color color, DateTime created, DateTime expires)> clickPositions =
    new List<(int x, int y, Color color, DateTime created, DateTime expires)>();
    static Thread? clickOverlayThread = null;
    static bool clickOverlayActive = false;
    static Form? clickOverlayForm = null;
    static readonly Color DEFAULT_LEFT_CLICK_COLOR = Color.FromArgb(180, 0, 255, 0);
    static readonly Color DEFAULT_RIGHT_CLICK_COLOR = Color.FromArgb(180, 255, 128, 0);
    static readonly Color WAYPOINT_CLICK_COLOR = Color.FromArgb(180, 255, 0, 255);
    static void StartClickOverlay()
    {
        lock (clickOverlayLock)
        {
            if (clickOverlayActive)
            {
                return;
            }
            clickPositions.Clear();
            clickOverlayActive = true;
            clickOverlayThread = new Thread(() =>
            {
                try
                {
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
                }
            });
            clickOverlayThread.IsBackground = true;
            clickOverlayThread.SetApartmentState(ApartmentState.STA);
            clickOverlayThread.Start();
        }
    }
    static void StopClickOverlay()
    {
        lock (clickOverlayLock)
        {
            if (!clickOverlayActive)
                return;
            clickOverlayActive = false;
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
        int exStyle = GetWindowLong(form.Handle, GWL_EXSTYLE);
        SetWindowLong(form.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        form.Paint += (sender, e) =>
        {
            try
            {
                lock (clickOverlayLock)
                {
                    if (targetWindow == IntPtr.Zero || clickPositions.Count == 0)
                        return;
                    GetWindowRect(targetWindow, out RECT gameRect);
                    foreach (var click in clickPositions)
                    {
                        if (DateTime.Now > click.expires)
                            continue;
                        int relX = click.x - gameRect.Left;
                        int relY = click.y - gameRect.Top;
                        if (relX >= 0 && relY >= 0 && relX < form.Width && relY < form.Height)
                        {
                            bool isWaypoint = click.color.R > 200 && click.color.G < 100 && click.color.B > 200;
                            int squareSize = isWaypoint ?
                                Math.Max(pixelSize, 20) :
                                Math.Max(pixelSize, 24);
                            int left = relX - (squareSize / 2);
                            int top = relY - (squareSize / 2);
                            int alpha;
                            if (isWaypoint)
                            {
                                alpha = 180;
                            }
                            else
                            {
                                TimeSpan timeRemaining = click.expires - DateTime.Now;
                                double remainingPercent = timeRemaining.TotalMilliseconds / CLICK_MARKER_LIFESPAN.TotalMilliseconds;
                                alpha = (int)(remainingPercent * 180);
                                alpha = Math.Max(50, Math.Min(255, alpha));
                            }
                            Color displayColor = Color.FromArgb(
                                alpha,
                                click.color.R,
                                click.color.G,
                                click.color.B
                            );
                            if (isWaypoint)
                            {
                                int diameter = squareSize;
                                int radius = diameter / 2;
                                using (SolidBrush brush = new SolidBrush(displayColor))
                                {
                                    e.Graphics.FillEllipse(brush, left, top, diameter, diameter);
                                }
                                using (Pen crosshairPen = new Pen(Color.FromArgb(alpha, 255, 255, 255), 1))
                                {
                                    e.Graphics.DrawLine(
                                        crosshairPen,
                                        left, top + radius,
                                        left + diameter, top + radius
                                    );
                                    e.Graphics.DrawLine(
                                        crosshairPen,
                                        left + radius, top,
                                        left + radius, top + diameter
                                    );
                                    e.Graphics.DrawEllipse(
                                        crosshairPen,
                                        left, top, diameter, diameter
                                    );
                                }
                            }
                            else
                            {
                                using (SolidBrush brush = new SolidBrush(displayColor))
                                {
                                    e.Graphics.FillRectangle(brush, left, top, squareSize, squareSize);
                                }
                                using (Pen borderPen = new Pen(Color.FromArgb(alpha, 255, 255, 255), 1))
                                {
                                    e.Graphics.DrawRectangle(borderPen, left, top, squareSize, squareSize);
                                }
                            }
                            TimeSpan remainingTime = click.expires - DateTime.Now;
                            if (remainingTime.TotalSeconds > 0.3)
                            {
                                string timeText = $"{remainingTime.TotalSeconds:F1}s";
                                using (Font font = new Font("Arial", isWaypoint ? 8 : 8, FontStyle.Bold))
                                {
                                    SizeF textSize = e.Graphics.MeasureString(timeText, font);
                                    float textX = left + (squareSize - textSize.Width) / 2;
                                    float textY = top + (squareSize - textSize.Height) / 2;
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
        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer
        {
            Interval = 100
        };
        DateTime lastCleanupTime = DateTime.MinValue;
        const int CLEANUP_INTERVAL_MS = 500;
        timer.Tick += (sender, e) =>
        {
            try
            {
                DateTime now = DateTime.Now;
                bool shouldUpdatePosition = false;
                bool shouldRemoveExpired = false;
                if ((now - lastCleanupTime).TotalMilliseconds >= CLEANUP_INTERVAL_MS)
                {
                    shouldUpdatePosition = true;
                    shouldRemoveExpired = true;
                    lastCleanupTime = now;
                }
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
                if (shouldRemoveExpired)
                {
                    lock (clickOverlayLock)
                    {
                        int countBefore = clickPositions.Count;
                        clickPositions.RemoveAll(click => now >= click.expires);
                        int removed = countBefore - clickPositions.Count;
                        if (removed > 0)
                        {
                        }
                    }
                }
                lock (clickOverlayLock)
                {
                    if (clickPositions.Count > 0)
                    {
                        form.Invalidate();
                    }
                }
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
        lock (clickOverlayLock)
        {
            clickOverlayForm = form;
        }
        timer.Start();
        form.Show();
        DateTime lastDoEventsTime = DateTime.MinValue;
        const int DO_EVENTS_INTERVAL_MS = 25;
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
                Thread.Sleep(5);
            }
        }
        timer.Stop();
        timer.Dispose();
        if (!form.IsDisposed)
        {
            form.Close();
            form.Dispose();
        }
    }
    static void RecordWaypointClick(int x, int y)
    {
        lock (clickOverlayLock)
        {
            if (!clickOverlayActive)
                return;
            POINT screenPoint = new POINT { X = x, Y = y };
            ClientToScreen(targetWindow, ref screenPoint);
            DateTime creationTime = DateTime.Now;
            DateTime expirationTime = creationTime.Add(TimeSpan.FromSeconds(0.5));
            clickPositions.Add((
                screenPoint.X,
                screenPoint.Y,
                WAYPOINT_CLICK_COLOR,
                creationTime,
                expirationTime
            ));
        }
    }
    static readonly TimeSpan CLICK_MARKER_LIFESPAN = TimeSpan.FromSeconds(0.8);
    static void RecordClickPosition(int x, int y, bool isLeftClick)
    {
        lock (clickOverlayLock)
        {
            if (!clickOverlayActive)
                return;
            POINT screenPoint = new POINT { X = x, Y = y };
            ClientToScreen(targetWindow, ref screenPoint);
            DateTime creationTime = DateTime.Now;
            DateTime expirationTime = creationTime.Add(CLICK_MARKER_LIFESPAN);
            Color clickColor = isLeftClick ? DEFAULT_LEFT_CLICK_COLOR : DEFAULT_RIGHT_CLICK_COLOR;
            clickPositions.Add((
                screenPoint.X,
                screenPoint.Y,
                clickColor,
                creationTime,
                expirationTime
            ));
        }
    }
    static bool lootRecognizerActive = true;
    static Thread? lootRecognizerThread = null;
    static readonly object lootRecognizerLock = new object();
    static Dictionary<string, Mat> lootTemplates = new Dictionary<string, Mat>();
    static string dropsDirectoryPath = "drops";
    static readonly TimeSpan LOOT_SCAN_INTERVAL = TimeSpan.FromSeconds(1);
    static readonly TimeSpan DRAG_OPERATION_COOLDOWN = TimeSpan.FromSeconds(1);
    static DateTime lastDragOperationTime = DateTime.MinValue;
    static double lootMatchThreshold = 0.92;
    static void LootRecognizerThread()
    {
        try
        {
            if (!Directory.Exists(dropsDirectoryPath))
            {
                Directory.CreateDirectory(dropsDirectoryPath);
            }
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
            if (lootTemplates.ContainsKey(PLATINUM_TEMPLATE_NAME))
            {
            }
            else
            {
                Console.WriteLine($"[LOOT] WARNING: Platinum template '{PLATINUM_TEMPLATE_NAME}' not found");
            }
            DateTime lastScanTime = DateTime.MinValue;
            while (memoryReadActive && lootRecognizerActive)
            {
                try
                {
                    DateTime now = DateTime.Now;
                    if (shouldScanInventoryForPlatinum && now >= lastInventoryScanTime)
                    {
                        ScanInventoryForPlatinum();
                    }
                    if ((now - lastScanTime) >= LOOT_SCAN_INTERVAL)
                    {
                        lastScanTime = now;
                        if (lootTemplates.Count > 0 && targetWindow != IntPtr.Zero)
                        {
                            ScanBackpackForLoot();
                        }
                    }
                    Sleep(200);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LOOT] Error in loot recognizer: {ex.Message}");
                    Sleep(1000);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LOOT] Error initializing loot recognizer: {ex.Message}");
        }
    }
    static void LoadLootTemplates()
    {
        try
        {
            foreach (var template in lootTemplates.Values)
            {
                template.Dispose();
            }
            lootTemplates.Clear();
            string[] imageFiles = Directory.GetFiles(dropsDirectoryPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => IsImageFile(f))
                .ToArray();
            foreach (string filePath in imageFiles)
            {
                try
                {
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    Mat template = CvInvoke.Imread(filePath, ImreadModes.Color);
                    if (template.IsEmpty)
                    {
                        continue;
                    }
                    lootTemplates[fileName] = template;
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
    static bool IsImageFile(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLower();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";
    }
    private static bool hasDebugImageBeenSaved = false;
    static void ScanBackpackForLoot()
    {
        var debugMode = false;
        try
        {
            UpdateUIPositions();
            if ((DateTime.Now - lastDragOperationTime) < DRAG_OPERATION_COOLDOWN)
            {
                return;
            }
            string debugDir = string.Empty;
            if (debugMode)
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                debugDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug", $"scan_{timestamp}");
                if (!Directory.Exists(debugDir)) Directory.CreateDirectory(debugDir);
            }
            GetClientRect(targetWindow, out RECT windowRect);
            int windowWidth = windowRect.Right - windowRect.Left;
            int windowHeight = windowRect.Bottom - windowRect.Top;
            int backpackScanLeft = firstSlotBpX - (pixelSize / 2);
            int backpackScanTop = firstSlotBpY - (pixelSize / 2);
            int backpackScanWidth = pixelSize * 4;
            int backpackScanHeight = pixelSize * 2;
            if (backpackScanLeft < 0) backpackScanLeft = 0;
            if (backpackScanTop < 0) backpackScanTop = 0;
            if (backpackScanLeft + backpackScanWidth > windowWidth) backpackScanWidth = windowWidth - backpackScanLeft;
            if (backpackScanTop + backpackScanHeight > windowHeight) backpackScanHeight = windowHeight - backpackScanTop;
            using (Mat backpackArea = CaptureGameAreaAsMat(targetWindow, backpackScanLeft, backpackScanTop, backpackScanWidth, backpackScanHeight))
            {
                if (backpackArea == null || backpackArea.IsEmpty)
                {
                    return;
                }
                if (debugMode)
                {
                    string scanAreaPath = Path.Combine(debugDir, "backpack_scan_area.png");
                    CvInvoke.Imwrite(scanAreaPath, backpackArea);
                    string templatesDir = Path.Combine(debugDir, "templates");
                    if (!Directory.Exists(templatesDir)) Directory.CreateDirectory(templatesDir);
                    foreach (var template in lootTemplates)
                    {
                        string templatePath = Path.Combine(templatesDir, $"{template.Key}.png");
                        CvInvoke.Imwrite(templatePath, template.Value);
                    }
                }
                Mat visualizationImage = debugMode ? backpackArea.Clone() : null;
                try
                {
                    foreach (var template in lootTemplates)
                    {
                        string itemName = template.Key;
                        Mat itemTemplate = template.Value;
                        using (Mat result = new Mat())
                        {
                            CvInvoke.MatchTemplate(backpackArea, itemTemplate, result, TemplateMatchingType.CcoeffNormed);
                            double minVal = 0, maxVal = 0;
                            Point minLoc = new Point(), maxLoc = new Point();
                            CvInvoke.MinMaxLoc(result, ref minVal, ref maxVal, ref minLoc, ref maxLoc);
                            if (debugMode)
                            {
                                using (Mat normalizedResult = new Mat())
                                {
                                    CvInvoke.Normalize(result, normalizedResult, 0, 255, NormType.MinMax);
                                    CvInvoke.ConvertScaleAbs(normalizedResult, normalizedResult, 1.0, 0.0);
                                    string resultPath = Path.Combine(debugDir, $"match_result_{itemName}.png");
                                    CvInvoke.Imwrite(resultPath, normalizedResult);
                                }
                                Rectangle matchRect = new Rectangle(maxLoc, new Size(itemTemplate.Width, itemTemplate.Height));
                                MCvScalar color = (maxVal >= lootMatchThreshold) ?
                                                new MCvScalar(0, 255, 0) :
                                                new MCvScalar(0, 0, 255);
                                CvInvoke.Rectangle(visualizationImage, matchRect, color, 2);
                                string matchText = $"{itemName}: {maxVal:F3}";
                                Point textPoint = new Point(matchRect.X, Math.Max(0, matchRect.Y - 5));
                                CvInvoke.PutText(visualizationImage, matchText, textPoint,
                                                FontFace.HersheyComplex, 0.5, color, 1);
                                if (maxVal >= 0.5)
                                {
                                    using (Mat matchedRegion = new Mat(backpackArea, matchRect))
                                    {
                                        string matchedRegionPath = Path.Combine(debugDir, $"matched_region_{itemName}_{maxVal:F3}.png");
                                        CvInvoke.Imwrite(matchedRegionPath, matchedRegion);
                                    }
                                }
                            }
                            if (maxVal >= lootMatchThreshold)
                            {
                                if (debugMode)
                                {
                                    string visualizationPath = Path.Combine(debugDir, "all_matches_visualization.png");
                                    CvInvoke.Imwrite(visualizationPath, visualizationImage);
                                    using (Mat fullScreenshot = CaptureGameAreaAsMat(targetWindow, 0, 0, windowWidth, windowHeight))
                                    {
                                        if (fullScreenshot != null && !fullScreenshot.IsEmpty)
                                        {
                                            int itemX = backpackScanLeft + maxLoc.X + (itemTemplate.Width / 2);
                                            int itemY = backpackScanTop + maxLoc.Y + (itemTemplate.Height / 2);
                                            int invX = inventoryX;
                                            int invY = inventoryY + pixelSize;
                                            CvInvoke.Circle(fullScreenshot, new Point(itemX, itemY), 10, new MCvScalar(0, 0, 255), 2);
                                            CvInvoke.Circle(fullScreenshot, new Point(invX, invY), 10, new MCvScalar(0, 255, 0), 2);
                                            CvInvoke.Line(fullScreenshot, new Point(itemX, itemY), new Point(invX, invY),
                                                        new MCvScalar(255, 255, 0), 2, LineType.AntiAlias);
                                            string dragPath = Path.Combine(debugDir, "drag_visualization.png");
                                            CvInvoke.Imwrite(dragPath, fullScreenshot);
                                        }
                                    }
                                }
                                Sleep(1);
                                int lootItemX = backpackScanLeft + maxLoc.X + (itemTemplate.Width / 2);
                                int lootItemY = backpackScanTop + maxLoc.Y + (itemTemplate.Height / 2);
                                int inventorySlotX = inventoryX;
                                int inventorySlotY = inventoryY + pixelSize + 30;
                                if (itemName.Equals("100gold", StringComparison.OrdinalIgnoreCase))
                                {
                                    VirtualRightClick(targetWindow, lootItemX, lootItemY);
                                    lastDragOperationTime = DateTime.Now;
                                }
                                else
                                {
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
                                    DragItemToDestination(lootItemX, lootItemY, invX, invY, itemName);
                                    lastDragOperationTime = DateTime.Now;
                                }
                                lastDragOperationTime = DateTime.Now;
                                break;
                            }
                        }
                    }
                    if (debugMode && visualizationImage != null)
                    {
                        string allMatchesPath = Path.Combine(debugDir, "all_matches_visualization.png");
                        CvInvoke.Imwrite(allMatchesPath, visualizationImage);
                    }
                }
                finally
                {
                    if (visualizationImage != null)
                    {
                        visualizationImage.Dispose();
                    }
                }
            }
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
    static Mat? CaptureGameAreaAsMat(IntPtr hWnd, int x, int y, int width, int height)
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
                return null;
            }
            hdcMemDC = CreateCompatibleDC(hdcWindow);
            if (hdcMemDC == IntPtr.Zero)
            {
                return null;
            }
            hBitmap = CreateCompatibleBitmap(hdcWindow, width, height);
            if (hBitmap == IntPtr.Zero)
            {
                return null;
            }
            hOld = SelectObject(hdcMemDC, hBitmap);
            bool success = BitBlt(hdcMemDC, 0, 0, width, height, hdcWindow, x, y, SRCCOPY);
            if (!success)
            {
                return null;
            }
            SelectObject(hdcMemDC, hOld);
            using (Bitmap bmp = Bitmap.FromHbitmap(hBitmap))
            {
                Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    result = new Mat(bmp.Height, bmp.Width, DepthType.Cv8U, 3, bmpData.Scan0, bmpData.Stride);
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
    static bool shouldScanInventoryForPlatinum = true;
    static readonly string PLATINUM_TEMPLATE_NAME = "100platinum";
    static readonly TimeSpan PLATINUM_SCAN_DELAY = TimeSpan.FromMilliseconds(1500);
    static DateTime lastInventoryScanTime = DateTime.MinValue;
    static void DragItemToDestination(int sourceX, int sourceY, int destX, int destY, string itemName)
    {
        try
        {
            IntPtr sourceLParam = MakeLParam(sourceX, sourceY);
            IntPtr destLParam = MakeLParam(destX, destY);
            SendMessage(targetWindow, WM_MOUSEMOVE, IntPtr.Zero, sourceLParam);
            Sleep(1);
            SendMessage(targetWindow, WM_LBUTTONDOWN, IntPtr.Zero, sourceLParam);
            Sleep(1);
            RecordClickPosition(sourceX, sourceY, true);
            SendMessage(targetWindow, WM_MOUSEMOVE, new IntPtr(MK_LBUTTON), destLParam);
            Sleep(1);
            SendMessage(targetWindow, WM_LBUTTONUP, IntPtr.Zero, destLParam);
            Sleep(1);
            RecordClickPosition(destX, destY, true);
            Sleep(1);
            if (itemName.Equals(PLATINUM_TEMPLATE_NAME, StringComparison.OrdinalIgnoreCase))
            {
                shouldScanInventoryForPlatinum = true;
                lastInventoryScanTime = DateTime.Now.Add(PLATINUM_SCAN_DELAY);
            }
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
            Sleep(1024);
            UpdateUIPositions();
            int invScanLeft = inventoryX - (pixelSize / 2);
            int invScanTop = inventoryY - (pixelSize / 2);
            int invScanWidth = pixelSize * 4;
            int invScanHeight = pixelSize * 3;
            GetClientRect(targetWindow, out RECT windowRect);
            int windowWidth = windowRect.Right - windowRect.Left;
            int windowHeight = windowRect.Bottom - windowRect.Top;
            if (invScanLeft < 0) invScanLeft = 0;
            if (invScanTop < 0) invScanTop = 0;
            if (invScanLeft + invScanWidth > windowWidth) invScanWidth = windowWidth - invScanLeft;
            if (invScanTop + invScanHeight > windowHeight) invScanHeight = windowHeight - invScanTop;
            using (Mat inventoryArea = CaptureGameAreaAsMat(targetWindow, invScanLeft, invScanTop, invScanWidth, invScanHeight))
            {
                if (inventoryArea == null || inventoryArea.IsEmpty)
                {
                    return;
                }
                if (lootTemplates.TryGetValue(PLATINUM_TEMPLATE_NAME, out Mat platinumTemplate))
                {
                    using (Mat result = new Mat())
                    {
                        CvInvoke.MatchTemplate(inventoryArea, platinumTemplate, result, TemplateMatchingType.CcoeffNormed);
                        double minVal = 0, maxVal = 0;
                        Point minLoc = new Point(), maxLoc = new Point();
                        CvInvoke.MinMaxLoc(result, ref minVal, ref maxVal, ref minLoc, ref maxLoc);
                        if (maxVal >= lootMatchThreshold)
                        {
                            int platinumX = invScanLeft + maxLoc.X + (platinumTemplate.Width / 2);
                            int platinumY = invScanTop + maxLoc.Y + (platinumTemplate.Height / 2);
                            VirtualRightClick(targetWindow, platinumX, platinumY);
                            shouldScanInventoryForPlatinum = true;
                        }
                        else
                        {
                        }
                    }
                }
                else
                {
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LOOT ERROR] Error scanning inventory for platinum: {ex.Message}");
        }
        finally
        {
            shouldScanInventoryForPlatinum = true;
        }
    }
    static readonly TimeSpan RING_OPERATION_COOLDOWN = TimeSpan.FromSeconds(2);
    static DateTime lastRingOperationTime = DateTime.MinValue;
    static DateTime lastRingScanTime = DateTime.MinValue;
    static readonly TimeSpan RING_SCAN_INTERVAL = TimeSpan.FromSeconds(1);
    static void ScanRingContainersForMisplacedRings()
    {
        try
        {
            DateTime now = DateTime.Now;
            if ((now - lastRingScanTime) < RING_SCAN_INTERVAL)
            {
                return;
            }
            lastRingScanTime = now;
            UpdateUIPositions();
            int stealthRingSlotX = inventoryX;
            int stealthRingSlotY = inventoryY;
            int lifeRingSlotX = inventoryX;
            int lifeRingSlotY = inventoryY + 3 * pixelSize + 15;
            bool stealthRingInLifeSlot = ScanSlotForItem("stealthring", lifeRingSlotX, lifeRingSlotY);
            bool lifeRingInStealthSlot = ScanSlotForItem("lifering", stealthRingSlotX, stealthRingSlotY);
            var stealthRingInBackpack = ScanSlotsForItemWithPosition("stealthring", lifeRingSlotX, lifeRingSlotY, 4);
            var lifeRingInBackpack = ScanSlotsForItemWithPosition("lifering", stealthRingSlotX, stealthRingSlotY, 4);
            if (stealthRingInLifeSlot && lifeRingInStealthSlot)
            {
                SwapRings(stealthRingSlotX, stealthRingSlotY, lifeRingSlotX, lifeRingSlotY);
            }
            else if (stealthRingInLifeSlot)
            {
                DragItemToDestination(lifeRingSlotX, lifeRingSlotY, stealthRingSlotX, stealthRingSlotY, "stealthring_correction");
                lastRingOperationTime = DateTime.Now;
            }
            else if (lifeRingInStealthSlot)
            {
                DragItemToDestination(stealthRingSlotX, stealthRingSlotY, lifeRingSlotX, lifeRingSlotY, "lifering_correction");
                lastRingOperationTime = DateTime.Now;
            }
            else if (stealthRingInBackpack.found)
            {
                DragItemToDestination(stealthRingInBackpack.slotX, stealthRingInBackpack.slotY, stealthRingSlotX, stealthRingSlotY, "stealthring_from_backpack");
                lastRingOperationTime = DateTime.Now;
            }
            else if (lifeRingInBackpack.found)
            {
                DragItemToDestination(lifeRingInBackpack.slotX, lifeRingInBackpack.slotY, lifeRingSlotX, lifeRingSlotY, "lifering_from_backpack");
                lastRingOperationTime = DateTime.Now;
            }
            else
            {
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RING CORRECTOR] Error scanning ring containers: {ex.Message}");
        }
    }
    static bool ScanSlotForItem(string itemName, int slotX, int slotY, int numSlotsToScan = 1)
    {
        try
        {
            for (int slotIndex = 0; slotIndex < numSlotsToScan; slotIndex++)
            {
                int currentSlotX = slotX + (slotIndex * pixelSize);
                int currentSlotY = slotY;
                int scanSize = pixelSize / 2;
                int scanLeft = currentSlotX - scanSize;
                int scanTop = currentSlotY - scanSize;
                int scanWidth = pixelSize;
                int scanHeight = pixelSize;
                GetClientRect(targetWindow, out RECT windowRect);
                int windowWidth = windowRect.Right - windowRect.Left;
                int windowHeight = windowRect.Bottom - windowRect.Top;
                if (scanLeft < 0) scanLeft = 0;
                if (scanTop < 0) scanTop = 0;
                if (scanLeft + scanWidth > windowWidth) scanWidth = windowWidth - scanLeft;
                if (scanTop + scanHeight > windowHeight) scanHeight = windowHeight - scanTop;
                using (Mat slotArea = CaptureGameAreaAsMat(targetWindow, scanLeft, scanTop, scanWidth, scanHeight))
                {
                    if (slotArea == null || slotArea.IsEmpty)
                    {
                        continue;
                    }
                    if (lootTemplates.TryGetValue(itemName, out Mat itemTemplate))
                    {
                        using (Mat result = new Mat())
                        {
                            CvInvoke.MatchTemplate(slotArea, itemTemplate, result, TemplateMatchingType.CcoeffNormed);
                            double minVal = 0, maxVal = 0;
                            Point minLoc = new Point(), maxLoc = new Point();
                            CvInvoke.MinMaxLoc(result, ref minVal, ref maxVal, ref minLoc, ref maxLoc);
                            if (maxVal >= lootMatchThreshold)
                            {
                                return true;
                            }
                        }
                    }
                }//hej to ja rysiu to moj kod prosze nie krasc//
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RING CORRECTOR] Error scanning slots for {itemName}: {ex.Message}");
            return false;
        }
    }
    static (bool found, int slotX, int slotY) ScanSlotsForItemWithPosition(string itemName, int startSlotX, int startSlotY, int numSlotsToScan = 1)
    {
        try
        {
            for (int slotIndex = 0; slotIndex < numSlotsToScan; slotIndex++)
            {
                int currentSlotX = startSlotX + (slotIndex * pixelSize);
                int currentSlotY = startSlotY;
                int scanSize = pixelSize / 2;
                int scanLeft = currentSlotX - scanSize;
                int scanTop = currentSlotY - scanSize;
                int scanWidth = pixelSize;
                int scanHeight = pixelSize;
                GetClientRect(targetWindow, out RECT windowRect);
                int windowWidth = windowRect.Right - windowRect.Left;
                int windowHeight = windowRect.Bottom - windowRect.Top;
                if (scanLeft < 0) scanLeft = 0;
                if (scanTop < 0) scanTop = 0;
                if (scanLeft + scanWidth > windowWidth) scanWidth = windowWidth - scanLeft;
                if (scanTop + scanHeight > windowHeight) scanHeight = windowHeight - scanTop;
                using (Mat slotArea = CaptureGameAreaAsMat(targetWindow, scanLeft, scanTop, scanWidth, scanHeight))
                {
                    if (slotArea == null || slotArea.IsEmpty)
                    {
                        continue;
                    }
                    if (lootTemplates.TryGetValue(itemName, out Mat itemTemplate))
                    {
                        using (Mat result = new Mat())
                        {
                            CvInvoke.MatchTemplate(slotArea, itemTemplate, result, TemplateMatchingType.CcoeffNormed);
                            double minVal = 0, maxVal = 0;
                            Point minLoc = new Point(), maxLoc = new Point();
                            CvInvoke.MinMaxLoc(result, ref minVal, ref maxVal, ref minLoc, ref maxLoc);
                            if (maxVal >= lootMatchThreshold)
                            {
                                return (true, currentSlotX, currentSlotY);
                            }
                        }
                    }
                }
            }
            return (false, 0, 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RING CORRECTOR] Error scanning slots for {itemName}: {ex.Message}");
            return (false, 0, 0);
        }
    }
    static void SwapRings(int slot1X, int slot1Y, int slot2X, int slot2Y)
    {
        try
        {
            int tempX = firstSlotBpX;
            int tempY = firstSlotBpY;
            DragItemToDestination(slot1X, slot1Y, tempX, tempY, "ring_to_temp");
            Sleep(1);
            DragItemToDestination(slot2X, slot2Y, slot1X, slot1Y, "ring_to_slot1");
            Sleep(1);
            DragItemToDestination(tempX, tempY, slot2X, slot2Y, "ring_to_slot2");
            lastRingOperationTime = DateTime.Now;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RING CORRECTOR] Error swapping rings: {ex.Message}");
        }
    }

    static List<HoleLocation> detectedHoles = new List<HoleLocation>();
    static Dictionary<string, bool> holeLocationCache = new Dictionary<string, bool>();

    // Define hole/stairs location structure
    public class HoleLocation
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int FromZ { get; set; }
        public int ToZ { get; set; }
        public HoleType Type { get; set; }

        public string GetLocationKey()
        {
            return $"{X},{Y}";
        }

        public override string ToString()
        {
            return $"{Type} at ({X},{Y}) from Z={FromZ} to Z={ToZ}";
        }
    }

    public enum HoleType
    {
        Up,    // Going up (F4)
        Down   // Going down (walking into)
    }

    // Call this function after loading waypoints to identify holes

    // Helper function to check if a position is a known hole
    static HoleLocation? GetHoleAtPosition(int x, int y, int currentZ)
    {
        string locationKey = $"{x},{y}";
        if (!holeLocationCache.ContainsKey(locationKey))
            return null;

        // Find the specific hole at this position that matches our current Z
        return detectedHoles.FirstOrDefault(h =>
            h.X == x && h.Y == y && h.FromZ == currentZ);
    }
}
