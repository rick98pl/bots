using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Emgu.CV.Reg;

class Program
{
    // [Keep all existing DLL imports unchanged]
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
    static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);



    // Add these P/Invoke declarations
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

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    const uint SRCCOPY = 0x00CC0020;





    // [Keep all existing structs and constants unchanged]
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

    // [Keep all constants unchanged]
    const int PROCESS_ALL_ACCESS = 0x001F0FFF;
    const uint WM_KEYDOWN = 0x0100;
    const uint WM_KEYUP = 0x0101;
    const uint WM_LBUTTONDOWN = 0x0201;
    const uint WM_LBUTTONUP = 0x0202;
    const uint WM_LBUTTONDBLCLK = 0x0203;
    const uint WM_RBUTTONDOWN = 0x0204;
    const uint WM_RBUTTONUP = 0x0205;
    const uint WM_RBUTTONDBLCLK = 0x0206;
    const uint WM_MBUTTONDOWN = 0x0207;
    const uint WM_MBUTTONUP = 0x0208;
    const uint WM_MBUTTONDBLCLK = 0x0209;
    const uint WM_MOUSEMOVE = 0x0200;
    const uint WM_MOUSEWHEEL = 0x020A;
    const uint WM_MOUSEHWHEEL = 0x020E;
    const int VK_F1 = 0x70;
    const int VK_F2 = 0x71;
    const int VK_F3 = 0x72;
    const int VK_F4 = 0x73;
    const int VK_F5 = 0x74;
    const int VK_F6 = 0x75;
    const int VK_F7 = 0x76;
    const int VK_F8 = 0x77;
    const int VK_F9 = 0x78;
    const int VK_F10 = 0x79;
    const int VK_F11 = 0x7A;
    const int VK_F12 = 0x7B;
    const int VK_LEFT = 0x25;
    const int VK_UP = 0x26;
    const int VK_RIGHT = 0x27;
    const int VK_DOWN = 0x28;
    const byte VK_ESCAPE = 0x1B;
    const int MK_LBUTTON = 0x0001;

    // [Keep all existing variables unchanged]
    static IntPtr targetWindow = IntPtr.Zero;
    static IntPtr processHandle = IntPtr.Zero;
    static IntPtr moduleBase = IntPtr.Zero;
    static int HP_THRESHOLD = 70;
    static int MANA_THRESHOLD = 1000;
    static double SOUL_THRESHOLD = 5.0;
    static IntPtr BASE_ADDRESS = 0x009432D0;
    static int HP_OFFSET = 1184;
    static int MAX_HP_OFFSET = 1192;
    static int MANA_OFFSET = 1240;
    static int MAX_MANA_OFFSET = 1248;
    static int SOUL_OFFSET = 1280;
    static IntPtr POSITION_X_OFFSET = 0x009435FC;
    static IntPtr POSITION_Y_OFFSET = 0x00943600;
    static IntPtr POSITION_Z_OFFSET = 0x00943604;
    static IntPtr targetIdOffset = 0x009432D4;
    static double curHP = 0, maxHP = 1;
    static double curMana = 0, maxMana = 1;
    static double curSoul = 0;
    static int currentX = 0, currentY = 0, currentZ = 0;
    static string processName = "RealeraDX";
    static DateTime lastHpAction = DateTime.MinValue;
    static DateTime lastManaAction = DateTime.MinValue;
    static bool programRunning = true;
    static bool itemDragInProgress = false;
    static int currentDragCount = 0;
    static bool actionSequenceRunning = false;
    static int currentActionIndex = 0;
    static int backpackX = 615;
    static int backpackY = 75;
    static int groundX = 300;
    static int groundY = 280;
    const int WAYPOINT_SIZE = 39;
    const int TOLERANCE = 0;
    static CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

    // Updated Action abstract class with retry support
    public abstract class Action
    {
        public int MaxRetries { get; set; } = 10;
        public abstract bool Execute();
        public abstract string GetDescription();

        // Method to check if the action was successful (override for specific actions)
        public virtual bool VerifySuccess()
        {
            return true; // Default implementation - consider action successful
        }
    }

    public class ScanBackpackAction : Action
    {
        public ScanBackpackAction()
        {
            MaxRetries = 3; // Optional: set retry attempts for this action
        }

        public override bool Execute()
        {
            Console.WriteLine("Scanning backpack for recognized items");
            ScanBackpackForRecognizedItems();
            return true;
        }

        public override bool VerifySuccess()
        {
            // Since this is just a scan operation, we could add verification logic here
            // For example, check if a drag operation actually happened
            return true; // For now, assume success
        }

        public override string GetDescription()
        {
            return "Scan backpack for items";
        }
    }

    // Updated MoveAction with proper verification
    public class MoveAction : Action
    {
        public int TargetX { get; set; }
        public int TargetY { get; set; }
        public int TargetZ { get; set; }
        public int TimeoutMs { get; set; } = 5000;

        public MoveAction(int x, int y, int z, int timeoutMs = 5000)
        {
            TargetX = x;
            TargetY = y;
            TargetZ = z;
            TimeoutMs = timeoutMs;
        }

        public override bool Execute()
        {
            Console.WriteLine($"Moving to position ({TargetX}, {TargetY}, {TargetZ})");

            // Click on the waypoint
            ClickWaypoint(TargetX, TargetY);

            // Wait a moment for the click to register
            Thread.Sleep(500);

            return true; // We'll verify success separately
        }

        public override bool VerifySuccess()
        {
            DateTime startTime = DateTime.Now;

            // Wait for the character to reach the waypoint
            while ((DateTime.Now - startTime).TotalMilliseconds < TimeoutMs)
            {
                ReadMemoryValues();
                if (IsAtPosition(TargetX, TargetY, TargetZ))
                {
                    Console.WriteLine($"Successfully reached position ({TargetX}, {TargetY}, {TargetZ})");
                    return true;
                }
                Thread.Sleep(100);
            }

            Console.WriteLine($"Failed to reach position ({TargetX}, {TargetY}, {TargetZ}) within {TimeoutMs}ms");
            return false;
        }

        public override string GetDescription()
        {
            return $"Move to ({TargetX}, {TargetY}, {TargetZ})";
        }
    }

    // Right-click action with verification
    public class RightClickAction : Action
    {
        public int DelayAfterMs { get; set; }

        public RightClickAction(int delayAfterMs = 100)
        {
            DelayAfterMs = delayAfterMs;
        }

        public override bool Execute()
        {
            Console.WriteLine("Right-clicking on character position");
            RightClickOnCharacter();
            return true;
        }

        public override bool VerifySuccess()
        {
            // For right-click, just wait the specified delay
            if (DelayAfterMs > 0)
            {
                Thread.Sleep(DelayAfterMs);
            }
            return true; // Right-click doesn't need complex verification
        }

        public override string GetDescription()
        {
            return "Right-click on character";
        }
    }

    // [Keep other action classes unchanged - DragAction, HotkeyAction, ArrowAction]
    public class DragAction : Action
    {
        public enum DragDirection
        {
            BackpackToGround,
            GroundToBackpack
        }

        public DragDirection Direction { get; set; }
        public int ItemCount { get; set; }
        public int DelayBetweenDrags { get; set; }

        public DragAction(DragDirection direction, int itemCount = 8, int delayBetweenDrags = 100)
        {
            Direction = direction;
            ItemCount = itemCount;
            DelayBetweenDrags = delayBetweenDrags;
        }

        public override bool Execute()
        {
            bool reverseDirection = Direction == DragDirection.GroundToBackpack;
            string directionName = reverseDirection ? "ground to backpack" : "backpack to ground";

            Console.WriteLine($"Dragging {ItemCount} items from {directionName}");

            try
            {
                RECT clientRect;
                GetClientRect(targetWindow, out clientRect);
                int localX = backpackX;
                int localY = backpackY;
                if (reverseDirection)
                {
                    localX += 125;
                    localY += 125;
                }

                POINT groundPoint = new POINT { X = groundX, Y = groundY };
                POINT backpackPoint = new POINT { X = localX, Y = localY };

                ClientToScreen(targetWindow, ref groundPoint);
                ClientToScreen(targetWindow, ref backpackPoint);

                POINT sourcePoint, destPoint;
                if (reverseDirection)
                {
                    sourcePoint = groundPoint;
                    destPoint = backpackPoint;
                }
                else
                {
                    sourcePoint = backpackPoint;
                    destPoint = groundPoint;
                }

                for (int i = 1; i <= ItemCount; i++)
                {
                    DragItem(sourcePoint.X, sourcePoint.Y, destPoint.X, destPoint.Y);
                    Console.WriteLine($"Drag #{i} completed... ({i}/{ItemCount})");

                    if (DelayBetweenDrags > 0 && i < ItemCount)
                    {
                        Thread.Sleep(DelayBetweenDrags);
                    }
                }

                Console.WriteLine($"All {ItemCount} item drags completed ({directionName}).");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during drag action: {ex.Message}");
                return false;
            }
        }

        public override string GetDescription()
        {
            string directionName = Direction == DragDirection.GroundToBackpack ? "ground to backpack" : "backpack to ground";
            return $"Drag {ItemCount} items from {directionName}";
        }
    }

    public class HotkeyAction : Action
    {
        public int KeyCode { get; set; }
        public int DelayMs { get; set; }

        public HotkeyAction(int keyCode, int delayMs = 100)
        {
            KeyCode = keyCode;
            DelayMs = delayMs;
        }

        public override bool Execute()
        {
            string keyName = GetKeyName(KeyCode);
            Console.WriteLine($"Pressing {keyName}");

            SendKeyPress(KeyCode);
            return true;
        }

        public override bool VerifySuccess()
        {
            if (DelayMs > 0)
            {
                Thread.Sleep(DelayMs);
            }
            return true;
        }

        public override string GetDescription()
        {
            return $"Press {GetKeyName(KeyCode)}";
        }

        private string GetKeyName(int keyCode)
        {
            switch (keyCode)
            {
                case VK_F1: return "F1";
                case VK_F2: return "F2";
                case VK_F3: return "F3";
                case VK_F4: return "F4";
                case VK_F5: return "F5";
                case VK_F6: return "F6";
                case VK_F7: return "F7";
                case VK_F8: return "F8";
                case VK_F9: return "F9";
                case VK_F10: return "F10";
                case VK_F11: return "F11";
                case VK_F12: return "F12";
                default: return $"Key {keyCode}";
            }
        }
    }

    public class ArrowAction : Action
    {
        public enum ArrowDirection
        {
            Left,
            Right,
            Up,
            Down
        }

        public ArrowDirection Direction { get; set; }
        public int DelayMs { get; set; }

        public ArrowAction(ArrowDirection direction, int delayMs = 100)
        {
            Direction = direction;
            DelayMs = delayMs;
        }

        public override bool Execute()
        {
            int keyCode = GetArrowKeyCode(Direction);
            string directionName = Direction.ToString();
            Console.WriteLine($"Pressing Arrow {directionName}");

            SendKeyPress(keyCode);
            return true;
        }

        public override bool VerifySuccess()
        {
            if (DelayMs > 0)
            {
                Thread.Sleep(DelayMs);
            }
            return true;
        }

        public override string GetDescription()
        {
            return $"Press Arrow {Direction}";
        }

        private int GetArrowKeyCode(ArrowDirection direction)
        {
            switch (direction)
            {
                case ArrowDirection.Left: return VK_LEFT;
                case ArrowDirection.Right: return VK_RIGHT;
                case ArrowDirection.Up: return VK_UP;
                case ArrowDirection.Down: return VK_DOWN;
                default: return VK_LEFT;
            }
        }
    }

    // Static array of actions
    static List<Action> actionSequence = new List<Action>();

    static void InitializeActionSequence()
    {
        // Clear any existing actions
        actionSequence.Clear();

        // Inside InitializeActionSequence()
        for (int i = 0; i < 20; i++)
        {
            actionSequence.Add(new ScanBackpackAction());
        }

        actionSequence.Add(new DragAction(DragAction.DragDirection.BackpackToGround, 8, 100));

        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));
        actionSequence.Add(new MoveAction(32597, 32747, 7));
        actionSequence.Add(new MoveAction(32597, 32752, 7));
        actionSequence.Add(new MoveAction(32604, 32753, 7));
        actionSequence.Add(new MoveAction(32606, 32758, 7));
        actionSequence.Add(new MoveAction(32612, 32762, 7));
        actionSequence.Add(new MoveAction(32619, 32762, 7));
        actionSequence.Add(new MoveAction(32621, 32767, 7));
        actionSequence.Add(new MoveAction(32620, 32771, 7));
        actionSequence.Add(new MoveAction(32618, 32771, 7));

        actionSequence.Add(new RightClickAction(200));
        //actionSequence.Add(new DragAction(DragAction.DragDirection.BackpackToGround, 8, 100));

        actionSequence.Add(new MoveAction(32624, 32773, 6));
        actionSequence.Add(new MoveAction(32627, 32772, 6));
        actionSequence.Add(new MoveAction(32625, 32769, 6));

        actionSequence.Add(new MoveAction(32627, 32772, 6));
        actionSequence.Add(new MoveAction(32624, 32773, 6));
        actionSequence.Add(new MoveAction(32618, 32772, 6));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Up, 200));

        actionSequence.Add(new MoveAction(32621, 32766, 7));
        actionSequence.Add(new MoveAction(32618, 32761, 7));
        actionSequence.Add(new MoveAction(32621, 32756, 7));
        actionSequence.Add(new MoveAction(32626, 32752, 7));
        actionSequence.Add(new MoveAction(32628, 32749, 7));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Right, 200));


        actionSequence.Add(new MoveAction(32632, 32744, 6));
        actionSequence.Add(new MoveAction(32636, 32741, 6));
        actionSequence.Add(new HotkeyAction(VK_F4, 800));
        actionSequence.Add(new MoveAction(32630, 32744, 6));
        actionSequence.Add(new MoveAction(32626, 32742, 6));
        actionSequence.Add(new RightClickAction(200));

        actionSequence.Add(new MoveAction(32621, 32741, 5));
        actionSequence.Add(new HotkeyAction(VK_F5, 800));
        actionSequence.Add(new HotkeyAction(VK_F6, 800));

        actionSequence.Add(new MoveAction(32626, 32741, 5));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));
        actionSequence.Add(new MoveAction(32630, 32747, 6));
        actionSequence.Add(new MoveAction(32629, 32748, 6));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));

        actionSequence.Add(new MoveAction(32622, 32755, 7));
        actionSequence.Add(new MoveAction(32617, 32760, 7));
        actionSequence.Add(new MoveAction(32610, 32762, 7));
        actionSequence.Add(new MoveAction(32605, 32757, 7));
        actionSequence.Add(new MoveAction(32598, 32752, 7));
        actionSequence.Add(new MoveAction(32597, 32750, 7));
        actionSequence.Add(new MoveAction(32595, 32745, 7));
        actionSequence.Add(new MoveAction(32599, 32743, 7));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));



        Console.WriteLine($"Initialized action sequence with {actionSequence.Count} actions:");
        for (int i = 0; i < actionSequence.Count; i++)
        {
            Console.WriteLine($"  {i + 1}: {actionSequence[i].GetDescription()}");
        }
    }

    // Updated ExecuteActionSequence with retry logic
    static void ExecuteActionSequence()
    {
        try
        {
            actionSequenceRunning = true;
            currentActionIndex = 0;

            Console.WriteLine("Action sequence started...");

            for (currentActionIndex = 0; currentActionIndex < actionSequence.Count; currentActionIndex++)
            {
                if (cancellationTokenSource.Token.IsCancellationRequested || !programRunning)
                {
                    Console.WriteLine("Action sequence cancelled.");
                    break;
                }

                var action = actionSequence[currentActionIndex];
                Console.WriteLine($"\nExecuting action {currentActionIndex + 1}/{actionSequence.Count}: {action.GetDescription()}");

                bool success = false;
                int retryCount = 0;

                // Retry logic
                while (!success && retryCount < action.MaxRetries)
                {
                    if (retryCount > 0)
                    {
                        Console.WriteLine($"Retry attempt {retryCount}/{action.MaxRetries} for action: {action.GetDescription()}");
                    }

                    // Execute the action
                    bool executeSuccess = action.Execute();

                    if (executeSuccess)
                    {
                        // Verify if the action was successful
                        success = action.VerifySuccess();

                        if (!success)
                        {
                            Console.WriteLine($"Action executed but verification failed.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Action execution failed.");
                    }

                    if (!success)
                    {
                        retryCount++;
                        if (retryCount < action.MaxRetries)
                        {
                            // Wait before retrying
                            Thread.Sleep(1000);
                        }
                    }
                }

                if (!success)
                {
                    Console.WriteLine($"Action {currentActionIndex + 1} failed after {action.MaxRetries} attempts. Exiting program.");
                    programRunning = false;
                    break;
                }

                // Read and display current coordinates after each successful action
                ReadMemoryValues();
                Console.WriteLine($"Current coordinates: ({currentX}, {currentY}, {currentZ})");

                // Small delay between actions
                Thread.Sleep(200);
            }

            if (currentActionIndex >= actionSequence.Count && programRunning)
            {
                Console.WriteLine("\nAction sequence completed successfully!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during action sequence: {ex.Message}");
        }
        finally
        {
            actionSequenceRunning = false;
            currentActionIndex = 0;
        }
    }

    static bool IsAtPosition(int targetX, int targetY, int targetZ)
    {
        ReadMemoryValues();
        return Math.Abs(currentX - targetX) <= TOLERANCE &&
               Math.Abs(currentY - targetY) <= TOLERANCE &&
               currentZ == targetZ;
    }

    // [Keep all other methods unchanged - Main, ClickWaypoint, RightClickOnCharacter, etc.]

    static DateTime lastDropScanTime = DateTime.MinValue;
    static readonly TimeSpan DROP_SCAN_INTERVAL = TimeSpan.FromSeconds(1);
    static void Main()
    {
        Console.WriteLine("Starting RealeraDX Auto-Potions...");
        ShowAllProcessesWithWindows();

        // Initialize action sequence
        InitializeActionSequence();

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

        ReadMemoryValues();
        Console.WriteLine($"Found RealeraDX process (ID: {process.Id})");
        Console.WriteLine($"Window handle: {targetWindow}");
        Console.WriteLine("\nThresholds:");
        Console.WriteLine($"HP: {HP_THRESHOLD}%");
        Console.WriteLine($"Mana: {MANA_THRESHOLD}");
        Console.WriteLine($"Soul: {SOUL_THRESHOLD} (absolute value)");
        Console.WriteLine($"X: {currentX} (absolute value)");
        Console.WriteLine($"Y: {currentY} (absolute value)");
        Console.WriteLine($"Z: {currentZ} (absolute value)");
        Console.WriteLine("\nControls:");
        Console.WriteLine("Q - Quit");
        Console.WriteLine("E - Drag items from backpack to ground (8x)");
        Console.WriteLine("R - Drag items from ground to backpack (8x)");
        Console.WriteLine("P - Execute action sequence");


        // Main loop
        while (programRunning)
        {
            try
            {
                // Check for quit key
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;

                    if (key == ConsoleKey.Q)
                    {
                        Console.WriteLine("\nQuitting...");
                        programRunning = false;
                        cancellationTokenSource.Cancel();
                        Environment.Exit(0);
                    }
                    else if (key == ConsoleKey.E)
                    {
                        if (!itemDragInProgress)
                        {
                            Console.WriteLine("Starting to drag items from backpack to ground (8x)...");
                            currentDragCount = 0;
                            Task.Run(() => StartItemDragging(false));
                        }
                        else
                        {
                            Console.WriteLine("Item dragging already in progress...");
                        }
                    }
                    else if (key == ConsoleKey.R)
                    {
                        if (!itemDragInProgress)
                        {
                            Console.WriteLine("Starting to drag items from ground to backpack (8x)...");
                            currentDragCount = 0;
                            Task.Run(() => StartItemDragging(true));
                        }
                        else
                        {
                            Console.WriteLine("Item dragging already in progress...");
                        }
                    }
                    else if (key == ConsoleKey.P)
                    {
                        if (!actionSequenceRunning)
                        {
                            Console.WriteLine("Starting action sequence...");
                            Task.Run(() => ExecuteActionSequence());
                        }
                        else
                        {
                            Console.WriteLine("Action sequence already in progress...");
                        }
                    }
                }

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

    // [All remaining methods stay the same]
    static bool ClickWaypoint(int targetX, int targetY)
    {
        SendKeyPress(VK_ESCAPE);
        ReadMemoryValues();
        GetClientRect(targetWindow, out RECT rect);

        int centerX = groundX;
        int centerY = groundY;
        int lParam = (centerX << 16) | (centerY & 0xFFFF);

        int diffX = targetX - currentX;
        int diffY = targetY - currentY;

        int screenX = centerX + (diffX * WAYPOINT_SIZE);
        int screenY = centerY + (diffY * WAYPOINT_SIZE);

        lParam = (screenY << 16) | (screenX & 0xFFFF);

        SendMessage(targetWindow, WM_MOUSEMOVE, IntPtr.Zero, lParam);
        SendMessage(targetWindow, WM_LBUTTONDOWN, 1, lParam);
        SendMessage(targetWindow, WM_LBUTTONUP, IntPtr.Zero, lParam);

        return true;
    }

    static void RightClickOnCharacter()
    {
        int lParam = (groundY << 16) | (groundX & 0xFFFF);

        SendMessage(targetWindow, WM_MOUSEMOVE, IntPtr.Zero, lParam);
        SendMessage(targetWindow, WM_RBUTTONDOWN, 1, lParam);
        SendMessage(targetWindow, WM_RBUTTONUP, IntPtr.Zero, lParam);

        Console.WriteLine($"Right-clicked on character at screen coordinates ({groundX}, {groundY})");
    }

    static void StartItemDragging(bool reverseDirection)
    {
        try
        {
            itemDragInProgress = true;
            string direction = reverseDirection ? "ground to backpack" : "backpack to ground";
            Console.WriteLine($"Item dragging task started ({direction})...");

            RECT clientRect;
            GetClientRect(targetWindow, out clientRect);
            int localX = backpackX;
            int localY = backpackY;
            if (reverseDirection)
            {
                localX += 125;
                localY += 125;
            }

            POINT groundPoint = new POINT { X = groundX, Y = groundY };
            POINT backpackPoint = new POINT { X = localX, Y = localY };

            ClientToScreen(targetWindow, ref groundPoint);
            ClientToScreen(targetWindow, ref backpackPoint);

            POINT sourcePoint, destPoint;
            if (reverseDirection)
            {
                sourcePoint = groundPoint;
                destPoint = backpackPoint;
            }
            else
            {
                sourcePoint = backpackPoint;
                destPoint = groundPoint;
            }

            const int MAX_DRAGS = 8;
            for (currentDragCount = 1; currentDragCount <= MAX_DRAGS; currentDragCount++)
            {
                if (cancellationTokenSource.Token.IsCancellationRequested || !programRunning)
                {
                    Console.WriteLine($"Item dragging interrupted during drag #{currentDragCount}");
                    cancellationTokenSource = new CancellationTokenSource();
                    break;
                }

                DragItem(sourcePoint.X, sourcePoint.Y, destPoint.X, destPoint.Y);
                Console.WriteLine($"Drag #{currentDragCount} completed... ({currentDragCount}/{MAX_DRAGS})");

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

    static void DragItem(int fromX, int fromY, int toX, int toY)
    {
        POINT sourcePoint = new POINT { X = fromX, Y = fromY };
        POINT destPoint = new POINT { X = toX, Y = toY };

        // DEBUG: Convert game window coords to screen coords and show cursor
        POINT sourceScreenPoint = sourcePoint;
        POINT destScreenPoint = destPoint;
        ClientToScreen(targetWindow, ref sourceScreenPoint);
        ClientToScreen(targetWindow, ref destScreenPoint);

        // DEBUG: Show source position
        Console.WriteLine($"[DEBUG] Moving cursor to source: Game({fromX}, {fromY}) -> Screen({sourceScreenPoint.X}, {sourceScreenPoint.Y})");
        //SetCursorPos(fromX, fromY);
        //Thread.Sleep(4000);

        // DEBUG: Show destination position
        Console.WriteLine($"[DEBUG] Moving cursor to destination: Game({toX}, {toY}) -> Screen({destScreenPoint.X}, {destScreenPoint.Y})");
        //SetCursorPos(destScreenPoint.X, destScreenPoint.Y);
        //Thread.Sleep(4000);

        // Continue with original drag logic
        ScreenToClient(targetWindow, ref sourcePoint);
        ScreenToClient(targetWindow, ref destPoint);

        IntPtr lParamFrom = (IntPtr)((sourcePoint.Y << 16) | (sourcePoint.X & 0xFFFF));
        IntPtr lParamTo = (IntPtr)((destPoint.Y << 16) | (destPoint.X & 0xFFFF));

        PostMessage(targetWindow, WM_MOUSEMOVE, IntPtr.Zero, lParamFrom);
        Thread.Sleep(1);

        PostMessage(targetWindow, WM_LBUTTONDOWN, IntPtr.Zero, lParamFrom);
        Thread.Sleep(1);

        PostMessage(targetWindow, WM_MOUSEMOVE, IntPtr.Zero, lParamTo);
        Thread.Sleep(1);

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

    static int ReadPositionAsDouble(int offset)
    {
        double value = ReadDouble(offset);
        return (int)Math.Round(value);
    }

    static void ReadMemoryValues()
    {
        curHP = ReadDouble(HP_OFFSET);
        maxHP = ReadDouble(MAX_HP_OFFSET);
        curMana = ReadDouble(MANA_OFFSET);
        maxMana = ReadDouble(MAX_MANA_OFFSET);
        curSoul = ReadDouble(SOUL_OFFSET);

        currentX = ReadInt32(POSITION_X_OFFSET);
        currentY = ReadInt32(POSITION_Y_OFFSET);
        currentZ = ReadInt32(POSITION_Z_OFFSET);
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

    static int ReadInt32(IntPtr offset)
    {
        IntPtr address = IntPtr.Add(moduleBase, (int)offset);
        byte[] buffer = new byte[4];
        if (ReadProcessMemory(processHandle, address, buffer, buffer.Length, out _))
            return BitConverter.ToInt32(buffer, 0);
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

    static int firstSlotBpX = 840;
    static int firstSlotBpY = 250;
    static int debugScreenshotCounter = 1;
    static void ScanBackpackForRecognizedItems()
    {
        try
        {
            ReadMemoryValues();

            int scanLeft = 780;
            int scanTop = 230;
            int scanWidth = 170;
            int scanHeight = 200;

            using (Mat backpackArea = CaptureGameAreaAsMat(targetWindow, scanLeft, scanTop, scanWidth, scanHeight))
            {
                if (backpackArea == null || backpackArea.IsEmpty)
                    return;

                SaveDebugScreenshot(backpackArea, scanLeft, scanTop, scanWidth, scanHeight);

                Point? backpackPosition = FindPurpleBackpack(backpackArea, scanLeft, scanTop);
                if (backpackPosition.HasValue)
                {
                    // Convert to game window coordinates
                    int itemX = scanLeft + backpackPosition.Value.X;
                    int itemY = scanTop + backpackPosition.Value.Y;

                    // Destination coordinates (left backpack)
                    int leftBackpackX = 730;
                    int leftBackpackY = 215;

                    // Convert client coordinates to screen coordinates before passing to DragItem
                    POINT sourcePoint = new POINT { X = itemX, Y = itemY };
                    POINT destPoint = new POINT { X = leftBackpackX, Y = leftBackpackY };

                    ClientToScreen(targetWindow, ref sourcePoint);
                    ClientToScreen(targetWindow, ref destPoint);

                    Console.WriteLine($"[DROPS] Converting scan coords ({backpackPosition.Value.X}, {backpackPosition.Value.Y}) to game coords ({itemX}, {itemY})");
                    Console.WriteLine($"[DROPS] Converting game coords to screen coords: ({itemX}, {itemY}) -> ({sourcePoint.X}, {sourcePoint.Y})");
                    Console.WriteLine($"[DROPS] Destination screen coords: ({leftBackpackX}, {leftBackpackY}) -> ({destPoint.X}, {destPoint.Y})");

                    // Pass screen coordinates to DragItem
                    DragItem(sourcePoint.X, sourcePoint.Y, destPoint.X, destPoint.Y);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DROPS] Error scanning backpack: {ex.Message}");
        }
    }

    static void SaveDebugScreenshot(Mat backpackArea, int scanLeft, int scanTop, int scanWidth, int scanHeight)
    {
        try
        {
            string debugDir = "debug_screenshots";
            if (!Directory.Exists(debugDir))
            {
                Directory.CreateDirectory(debugDir);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = Path.Combine(debugDir, $"backpack_scan_{debugScreenshotCounter:D3}_{timestamp}.png");

            CvInvoke.Imwrite(filename, backpackArea);

            Console.WriteLine($"[DEBUG] Screenshot saved: {filename}");
            Console.WriteLine($"[DEBUG] Scan area: Left={scanLeft}, Top={scanTop}, Width={scanWidth}, Height={scanHeight}");

            debugScreenshotCounter++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error saving screenshot: {ex.Message}");
        }
    }

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
            Console.WriteLine($"[CAPTURE] Screenshot error: {ex.Message}");
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

    static Point? FindPurpleBackpack(Mat scanArea, int scanLeft, int scanTop)
    {
        try
        {
            string recognizedItemsPath = "recognizedItems";
            string purpleBackpackPath = Path.Combine(recognizedItemsPath, "purplebackpack.png");

            if (!File.Exists(purpleBackpackPath))
            {
                Console.WriteLine("[DROPS] purplebackpack.png not found in recognizedItems folder");
                return null;
            }

            using (Mat purpleBackpackTemplate = CvInvoke.Imread(purpleBackpackPath, ImreadModes.Color))
            {
                if (purpleBackpackTemplate.IsEmpty)
                    return null;

                using (Mat result = new Mat())
                {
                    CvInvoke.MatchTemplate(scanArea, purpleBackpackTemplate, result, TemplateMatchingType.CcoeffNormed);

                    double minVal = 0, maxVal = 0;
                    Point minLoc = new Point(), maxLoc = new Point();
                    CvInvoke.MinMaxLoc(result, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

                    if (maxVal >= 0.95)
                    {
                        // Create debug image showing found item
                        Mat debugImage = scanArea.Clone();
                        Rectangle foundRect = new Rectangle(maxLoc.X, maxLoc.Y, purpleBackpackTemplate.Width, purpleBackpackTemplate.Height);
                        CvInvoke.Rectangle(debugImage, foundRect, new MCvScalar(0, 255, 0), 2);
                        string confidenceText = $"Conf: {maxVal:F3}";
                        CvInvoke.PutText(debugImage, confidenceText, new Point(foundRect.X, foundRect.Y - 10),
                            FontFace.HersheyComplex, 0.5, new MCvScalar(0, 255, 0), 2);
                        SaveFoundItemDebugScreenshot(debugImage);
                        debugImage.Dispose();

                        // Return the center of the found backpack in scan area coordinates
                        // (NOT game window coordinates)
                        Point backpackCenter = new Point(
                            maxLoc.X + purpleBackpackTemplate.Width / 2,
                            maxLoc.Y + purpleBackpackTemplate.Height / 2
                        );

                        Console.WriteLine($"[DROPS] Found purple backpack at scan area position ({backpackCenter.X}, {backpackCenter.Y}) with confidence {maxVal:F2}");
                        return backpackCenter;
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DROPS] Error finding purple backpack: {ex.Message}");
            return null;
        }
    }

    static void SaveFoundItemDebugScreenshot(Mat debugImage)
    {
        try
        {
            string debugDir = "debug_screenshots";
            if (!Directory.Exists(debugDir))
            {
                Directory.CreateDirectory(debugDir);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string filename = Path.Combine(debugDir, $"found_item_{timestamp}.png");

            CvInvoke.Imwrite(filename, debugImage);

            Console.WriteLine($"[DEBUG] Found item screenshot saved: {filename}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error saving found item screenshot: {ex.Message}");
        }
    }

}
