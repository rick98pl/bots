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
using System.Text.Json;
using Emgu.CV.Dnn;

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
    const int VK_F13 = 0x7C;
    const int VK_F14 = 0x7D;
    const int LEFT_BRACKET = 0xDB;   // [ { key
    const int RIGHT_BRACKET = 0xDD;   // ] } key
    const int BACKSLASH = 0xDC;   // \ | key
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
    static double SOUL_THRESHOLD = 5;
    static IntPtr BASE_ADDRESS = 0x009432D0;
    static int HP_OFFSET = 1184;
    static int MAX_HP_OFFSET = 1192;
    static int MANA_OFFSET = 1240;
    static int MAX_MANA_OFFSET = 1248;
    static int SOUL_OFFSET = 1280;
    static int INVIS_OFFSET = 84;
    static int SPEED_OFFSET = 176;

    static IntPtr POSITION_X_OFFSET = 0x009435FC;
    static IntPtr POSITION_Y_OFFSET = 0x00943600;
    static IntPtr POSITION_Z_OFFSET = 0x00943604;
    static IntPtr TARGET_ID_OFFSET = 0x009432D4;
    static double curHP = 0, maxHP = 1;
    static double curMana = 0, maxMana = 1;
    static double curSoul = 0;
    static int currentX = 0, currentY = 0, currentZ = 0, targetId = 0, invisibilityCode = 0, speed = 0;
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
        public int TimeoutMs { get; set; } = 3000;

        public MoveAction(int x, int y, int z, int timeoutMs = 3000)
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
                    Thread.Sleep(600);
                    return true;
                }
                
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

        public enum DragBackpack
        {
            MANAS,
            SD
        }

        public DragDirection Direction { get; set; }
        public DragBackpack Backpack { get; set; }
        public int ItemCount { get; set; }
        public int DelayBetweenDrags { get; set; }

        public DragAction(DragDirection direction, DragBackpack backpack, int itemCount = 8, int delayBetweenDrags = 100)
        {
            Direction = direction;
            Backpack = backpack;
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
                

                int groundYLocal = groundY;
                if(directionName == "backpack to ground" && Backpack == DragBackpack.MANAS)
                {
                    groundYLocal = groundY + 4 * WAYPOINT_SIZE + 5;
                }

                int localX = backpackX;
                int localY = backpackY;
                if (Backpack == DragBackpack.SD)
                {
                    localX = 800;
                    localY = 245;
                }
                RECT clientRect;
                GetClientRect(targetWindow, out clientRect);
               
                if (reverseDirection)
                {
                    localX += 125;
                    localY += 125;
                }

                POINT groundPoint = new POINT { X = groundX, Y = groundYLocal };
                POINT backpackPoint = new POINT { X = localX, Y = localY };

                ClientToScreen(targetWindow, ref groundPoint);
                ClientToScreen(targetWindow, ref backpackPoint);

                //SetCursorPos(backpackPoint.X, backpackPoint.Y);
                //Thread.Sleep(4000);

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
                case VK_F13: return "F13";
                case VK_F14: return "F14";
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

    static void tarantulaSeqeunce()
    {
        // Clear any existing actions
        actionSequence.Clear();

        //// Base coordinates
        int baseX = 32597, baseY = 32747, baseZ = 7;

  
        actionSequence.Add(new FightTarantulasAction());



        actionSequence.Add(new HotkeyAction(VK_F12, 800)); //exani tera
        actionSequence.Add(new HotkeyAction(VK_F12, 800)); //exani tera


        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200)); //down the hole

        actionSequence.Add(new HotkeyAction(VK_F12, 800)); //exani tera
        actionSequence.Add(new HotkeyAction(VK_F12, 800)); //exani tera

        //actionSequence.Add(new HotkeyAction(VK_F11, 800)); //utana vid

        actionSequence.Add(new MoveAction(32817, 32809, 7));

        //actionSequence.Add(new HotkeyAction(BACKSLASH, 800)); //utanigranhur

        actionSequence.Add(new MoveAction(32814, 32809, 7));
        actionSequence.Add(new MoveAction(32807, 32810, 7));
        actionSequence.Add(new MoveAction(32801, 32811, 7));
        actionSequence.Add(new MoveAction(32795, 32809, 7));
        actionSequence.Add(new MoveAction(32789, 32807, 7));
        actionSequence.Add(new MoveAction(32782, 32803, 7));
        actionSequence.Add(new MoveAction(32776, 32803, 7));
        actionSequence.Add(new MoveAction(32769, 32805, 7));
        actionSequence.Add(new MoveAction(32762, 32807, 7));
        actionSequence.Add(new MoveAction(32755, 32807, 7));
        actionSequence.Add(new MoveAction(32748, 32811, 7));
        actionSequence.Add(new MoveAction(32741, 32807, 7));
        actionSequence.Add(new MoveAction(32735, 32803, 7));
        actionSequence.Add(new MoveAction(32728, 32799, 7));
        actionSequence.Add(new MoveAction(32723, 32794, 7));
        actionSequence.Add(new MoveAction(32716, 32791, 7));
        actionSequence.Add(new MoveAction(32713, 32786, 7));
        actionSequence.Add(new MoveAction(32710, 32788, 7));
        actionSequence.Add(new MoveAction(32706, 32785, 7));
        actionSequence.Add(new MoveAction(32699, 32784, 7));
        actionSequence.Add(new MoveAction(32692, 32783, 7));
        actionSequence.Add(new MoveAction(32685, 32781, 7));
        actionSequence.Add(new MoveAction(32679, 32777, 7));


        actionSequence.Add(new HotkeyAction(VK_F8, 800)); //bring me to centre

        actionSequence.Add(new DragAction(DragAction.DragDirection.BackpackToGround,
      DragAction.DragBackpack.MANAS, 8, 100)); //water

        actionSequence.Add(new MoveAction(32622, 32769, 7));

        actionSequence.Add(new MoveAction(baseX + 24, baseY + 19, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 21, baseY + 14, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 24, baseY + 9, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 29, baseY + 5, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 31, baseY + 2, baseZ + 0));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Right, 200));
        actionSequence.Add(new MoveAction(baseX + 35, baseY - 3, baseZ - 1));
        actionSequence.Add(new MoveAction(baseX + 39, baseY - 6, baseZ - 1));

        actionSequence.Add(new HotkeyAction(VK_F4, 800)); //money withdraw

        actionSequence.Add(new MoveAction(baseX + 33, baseY - 3, baseZ - 1));
        actionSequence.Add(new MoveAction(baseX + 29, baseY - 5, baseZ - 1));
        actionSequence.Add(new RightClickAction(200));
        actionSequence.Add(new MoveAction(baseX + 24, baseY - 6, baseZ - 2));

        actionSequence.Add(new HotkeyAction(VK_F5, 800)); //blanks

        actionSequence.Add(new HotkeyAction(VK_F9, 800)); //fluids

        for (int i = 0; i < 20; i++)
        {
            actionSequence.Add(new ScanBackpackAction());
        }

        actionSequence.Add(new MoveAction(baseX + 29, baseY - 6, baseZ - 2));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));
        actionSequence.Add(new MoveAction(baseX + 33, baseY + 0, baseZ - 1));
        actionSequence.Add(new MoveAction(baseX + 32, baseY + 1, baseZ - 1));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));
        actionSequence.Add(new MoveAction(baseX + 25, baseY + 8, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 20, baseY + 13, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 13, baseY + 15, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 8, baseY + 10, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 1, baseY + 5, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 0, baseY + 3, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX - 2, baseY - 2, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 2, baseY - 4, baseZ + 0));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));

        Console.WriteLine($"Initialized action sequence with {actionSequence.Count} actions:");
        for (int i = 0; i < actionSequence.Count; i++)
        {
            Console.WriteLine($"  {i + 1}: {actionSequence[i].GetDescription()}");
        }
    }

    static void InitializeActionSequence()
    {
        // Clear any existing actions
        actionSequence.Clear();

        //// Base coordinates
        int baseX = 32597, baseY = 32747, baseZ = 7;

        // Inside InitializeActionSequence()
        for (int i = 0; i < 20; i++)
        {
            actionSequence.Add(new ScanBackpackAction());
        }
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));

        //actionSequence.Add(new HotkeyAction(BACKSLASH, 800)); //utanigranhur

        actionSequence.Add(new MoveAction(baseX + 0, baseY + 0, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 0, baseY + 5, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 7, baseY + 6, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 9, baseY + 11, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 15, baseY + 15, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 22, baseY + 15, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 24, baseY + 20, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 23, baseY + 24, baseZ + 0));
        //actionSequence.Add(new MoveAction(baseX + 21, baseY + 24, baseZ + 0));


        actionSequence.Add(new MoveAction(32624, 32769, 7));
        actionSequence.Add(new MoveAction(32631, 32769, 7));
        actionSequence.Add(new MoveAction(32638, 32769, 7));

        actionSequence.Add(new RightClickAction(200)); //up the ladder 
        actionSequence.Add(new RightClickAction(200)); //up the ladder 
        actionSequence.Add(new RightClickAction(200)); //up the ladder 

        actionSequence.Add(new MoveAction(32635, 32773, 6));
        actionSequence.Add(new MoveAction(32630, 32773, 6));
        actionSequence.Add(new MoveAction(32627, 32772, 6));



        for (int i = 0; i < 20; i++)
        {
            actionSequence.Add(new ScanBackpackAction());
        }

        actionSequence.Add(new DragAction(DragAction.DragDirection.BackpackToGround,
            DragAction.DragBackpack.MANAS, 8, 100)); //water

        actionSequence.Add(new MoveAction(32625, 32769, 6));
        actionSequence.Add(new DragAction(DragAction.DragDirection.BackpackToGround,
            DragAction.DragBackpack.SD, 2, 100));

        actionSequence.Add(new MoveAction(32627, 32772, 6));
        actionSequence.Add(new MoveAction(32632, 32773, 6));
        actionSequence.Add(new MoveAction(32638, 32773, 6));
        actionSequence.Add(new MoveAction(32638, 32770, 6));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Up, 200)); //down the ladder

        //actionSequence.Add(new HotkeyAction(BACKSLASH, 800)); //utanigranhur

        actionSequence.Add(new MoveAction(32631, 32768, 7));
        actionSequence.Add(new MoveAction(32624, 32769, 7));


        actionSequence.Add(new MoveAction(baseX + 24, baseY + 19, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 21, baseY + 14, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 24, baseY + 9, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 29, baseY + 5, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 31, baseY + 2, baseZ + 0));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Right, 200));
        actionSequence.Add(new MoveAction(baseX + 35, baseY - 3, baseZ - 1));
        actionSequence.Add(new MoveAction(baseX + 39, baseY - 6, baseZ - 1));

        actionSequence.Add(new HotkeyAction(LEFT_BRACKET, 800)); //money withdraw

        actionSequence.Add(new MoveAction(baseX + 33, baseY - 3, baseZ - 1));
        actionSequence.Add(new MoveAction(baseX + 29, baseY - 5, baseZ - 1));
        actionSequence.Add(new RightClickAction(200));
        actionSequence.Add(new MoveAction(baseX + 24, baseY - 6, baseZ - 2));

        actionSequence.Add(new HotkeyAction(RIGHT_BRACKET, 800)); //fluids

        for (int i = 0; i < 20; i++)
        {
            actionSequence.Add(new ScanBackpackAction());
        }

        //actionSequence.Add(new HotkeyAction(BACKSLASH, 800)); //utanigranhur

        actionSequence.Add(new MoveAction(baseX + 29, baseY - 6, baseZ - 2));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));
        actionSequence.Add(new MoveAction(baseX + 33, baseY + 0, baseZ - 1));
        actionSequence.Add(new MoveAction(baseX + 32, baseY + 1, baseZ - 1));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));

        actionSequence.Add(new MoveAction(32629, 32754, 7));
        actionSequence.Add(new MoveAction(32629, 32758, 7));
        actionSequence.Add(new MoveAction(32624, 32762, 7));
        actionSequence.Add(new MoveAction(32621, 32766, 7));
        actionSequence.Add(new MoveAction(32621, 32770, 7));
        actionSequence.Add(new MoveAction(32627, 32769, 7));


        actionSequence.Add(new HotkeyAction(VK_F7, 800)); //bring me to east

        //actionSequence.Add(new HotkeyAction(BACKSLASH, 800)); //utanigranhur


        actionSequence.Add(new MoveAction(32685, 32781, 7));

        //actionSequence.Add(new HotkeyAction(VK_F11, 800)); //utana vid

        actionSequence.Add(new MoveAction(32692, 32783, 7));
        actionSequence.Add(new MoveAction(32699, 32784, 7));

        actionSequence.Add(new MoveAction(32706, 32785, 7));
        actionSequence.Add(new MoveAction(32710, 32788, 7));
        actionSequence.Add(new MoveAction(32713, 32786, 7));
        actionSequence.Add(new MoveAction(32716, 32791, 7));
        actionSequence.Add(new MoveAction(32723, 32794, 7));
        actionSequence.Add(new MoveAction(32728, 32799, 7));
        actionSequence.Add(new MoveAction(32735, 32803, 7));
        actionSequence.Add(new MoveAction(32741, 32807, 7));

        //actionSequence.Add(new HotkeyAction(BACKSLASH, 800)); //utanigranhur

        actionSequence.Add(new MoveAction(32748, 32811, 7));
        actionSequence.Add(new MoveAction(32755, 32807, 7));
        actionSequence.Add(new MoveAction(32762, 32807, 7));
        actionSequence.Add(new MoveAction(32769, 32805, 7));
        actionSequence.Add(new MoveAction(32776, 32803, 7));
        actionSequence.Add(new MoveAction(32782, 32803, 7));
        actionSequence.Add(new MoveAction(32789, 32807, 7));
        actionSequence.Add(new MoveAction(32795, 32809, 7));

        //actionSequence.Add(new HotkeyAction(BACKSLASH, 800)); //utanigranhur

        actionSequence.Add(new MoveAction(32801, 32811, 7));
        actionSequence.Add(new MoveAction(32807, 32810, 7));
        actionSequence.Add(new MoveAction(32814, 32809, 7));
        actionSequence.Add(new MoveAction(32817, 32809, 7));



        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Right, 200)); //down the hole

        ////HERE SHOULD FIGHT TARANTULAS UNTIL 200 SOUL
        actionSequence.Add(new FightTarantulasAction());



        actionSequence.Add(new HotkeyAction(VK_F12, 800)); //exani tera
        actionSequence.Add(new HotkeyAction(VK_F12, 800)); //exani tera


        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200)); //down the hole

        actionSequence.Add(new HotkeyAction(VK_F12, 800)); //exani tera
        actionSequence.Add(new HotkeyAction(VK_F12, 800)); //exani tera

        //actionSequence.Add(new HotkeyAction(VK_F11, 800)); //utana vid

        actionSequence.Add(new MoveAction(32817, 32809, 7));

        //actionSequence.Add(new HotkeyAction(BACKSLASH, 800)); //utanigranhur

        actionSequence.Add(new MoveAction(32814, 32809, 7));
        actionSequence.Add(new MoveAction(32807, 32810, 7));
        actionSequence.Add(new MoveAction(32801, 32811, 7));
        actionSequence.Add(new MoveAction(32795, 32809, 7));
        actionSequence.Add(new MoveAction(32789, 32807, 7));
        actionSequence.Add(new MoveAction(32782, 32803, 7));
        actionSequence.Add(new MoveAction(32776, 32803, 7));
        actionSequence.Add(new MoveAction(32769, 32805, 7));
        actionSequence.Add(new MoveAction(32762, 32807, 7));
        actionSequence.Add(new MoveAction(32755, 32807, 7));
        actionSequence.Add(new MoveAction(32748, 32811, 7));
        actionSequence.Add(new MoveAction(32741, 32807, 7));
        actionSequence.Add(new MoveAction(32735, 32803, 7));
        actionSequence.Add(new MoveAction(32728, 32799, 7));
        actionSequence.Add(new MoveAction(32723, 32794, 7));
        actionSequence.Add(new MoveAction(32716, 32791, 7));
        actionSequence.Add(new MoveAction(32713, 32786, 7));
        actionSequence.Add(new MoveAction(32710, 32788, 7));
        actionSequence.Add(new MoveAction(32706, 32785, 7));
        actionSequence.Add(new MoveAction(32699, 32784, 7));
        actionSequence.Add(new MoveAction(32692, 32783, 7));
        actionSequence.Add(new MoveAction(32685, 32781, 7));
        actionSequence.Add(new MoveAction(32679, 32777, 7));


        actionSequence.Add(new HotkeyAction(VK_F8, 800)); //bring me to centre

        actionSequence.Add(new DragAction(DragAction.DragDirection.BackpackToGround,
      DragAction.DragBackpack.MANAS, 8, 100)); //water

        actionSequence.Add(new MoveAction(32622, 32769, 7));

        actionSequence.Add(new MoveAction(baseX + 24, baseY + 19, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 21, baseY + 14, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 24, baseY + 9, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 29, baseY + 5, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 31, baseY + 2, baseZ + 0));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Right, 200));
        actionSequence.Add(new MoveAction(baseX + 35, baseY - 3, baseZ - 1));
        actionSequence.Add(new MoveAction(baseX + 39, baseY - 6, baseZ - 1));

        actionSequence.Add(new HotkeyAction(VK_F4, 800)); //money withdraw

        actionSequence.Add(new MoveAction(baseX + 33, baseY - 3, baseZ - 1));
        actionSequence.Add(new MoveAction(baseX + 29, baseY - 5, baseZ - 1));
        actionSequence.Add(new RightClickAction(200));
        actionSequence.Add(new MoveAction(baseX + 24, baseY - 6, baseZ - 2));

        actionSequence.Add(new HotkeyAction(VK_F5, 800)); //blanks

        actionSequence.Add(new HotkeyAction(VK_F9, 800)); //fluids

        for (int i = 0; i < 20; i++)
        {
            actionSequence.Add(new ScanBackpackAction());
        }

        actionSequence.Add(new MoveAction(baseX + 29, baseY - 6, baseZ - 2));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));
        actionSequence.Add(new MoveAction(baseX + 33, baseY + 0, baseZ - 1));
        actionSequence.Add(new MoveAction(baseX + 32, baseY + 1, baseZ - 1));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));
        actionSequence.Add(new MoveAction(baseX + 25, baseY + 8, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 20, baseY + 13, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 13, baseY + 15, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 8, baseY + 10, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 1, baseY + 5, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 0, baseY + 3, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX - 2, baseY - 2, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 2, baseY - 4, baseZ + 0));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));

        Console.WriteLine($"Initialized action sequence with {actionSequence.Count} actions:");
        for (int i = 0; i < actionSequence.Count; i++)
        {
            Console.WriteLine($"  {i + 1}: {actionSequence[i].GetDescription()}");
        }
    }

    static void InitializeMiddleSequence()
    {
        //// Clear any existing actions
        actionSequence.Clear();

        ////// Base coordinates
        int baseX = 32597, baseY = 32747, baseZ = 7;

        // Inside InitializeActionSequence()
        for (int i = 0; i < 20; i++)
        {
            actionSequence.Add(new ScanBackpackAction());
        }
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));

        //actionSequence.Add(new HotkeyAction(BACKSLASH, 800)); //utanigranhur

        actionSequence.Add(new MoveAction(baseX + 0, baseY + 0, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 0, baseY + 5, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 7, baseY + 6, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 9, baseY + 11, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 15, baseY + 15, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 22, baseY + 15, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 24, baseY + 20, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 23, baseY + 24, baseZ + 0));
        //actionSequence.Add(new MoveAction(baseX + 21, baseY + 24, baseZ + 0));


        actionSequence.Add(new MoveAction(32624, 32769, 7));
        actionSequence.Add(new MoveAction(32631, 32769, 7));
        actionSequence.Add(new MoveAction(32638, 32769, 7));

        actionSequence.Add(new RightClickAction(200)); //up the ladder 
        actionSequence.Add(new RightClickAction(200)); //up the ladder 
        actionSequence.Add(new RightClickAction(200)); //up the ladder 

        actionSequence.Add(new MoveAction(32635, 32773, 6));
        actionSequence.Add(new MoveAction(32630, 32773, 6));
        actionSequence.Add(new MoveAction(32627, 32772, 6));



        for (int i = 0; i < 20; i++)
        {
            actionSequence.Add(new ScanBackpackAction());
        }

        actionSequence.Add(new DragAction(DragAction.DragDirection.BackpackToGround,
            DragAction.DragBackpack.MANAS, 8, 100)); //water

        actionSequence.Add(new MoveAction(32625, 32769, 6));
        actionSequence.Add(new DragAction(DragAction.DragDirection.BackpackToGround,
            DragAction.DragBackpack.SD, 2, 100));

        actionSequence.Add(new MoveAction(32627, 32772, 6));
        actionSequence.Add(new MoveAction(32632, 32773, 6));
        actionSequence.Add(new MoveAction(32638, 32773, 6));
        actionSequence.Add(new MoveAction(32638, 32770, 6));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Up, 200)); //down the ladder

        actionSequence.Add(new MoveAction(32632, 32768, 7));
        actionSequence.Add(new MoveAction(32626, 32769, 7));
        actionSequence.Add(new MoveAction(32622, 32769, 7));


        actionSequence.Add(new MoveAction(baseX + 24, baseY + 19, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 21, baseY + 14, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 24, baseY + 9, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 29, baseY + 5, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 31, baseY + 2, baseZ + 0));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Right, 200));
        actionSequence.Add(new MoveAction(baseX + 35, baseY - 3, baseZ - 1));
        actionSequence.Add(new MoveAction(baseX + 39, baseY - 6, baseZ - 1));

        actionSequence.Add(new HotkeyAction(VK_F4, 800)); //money withdraw

        actionSequence.Add(new MoveAction(baseX + 33, baseY - 3, baseZ - 1));
        actionSequence.Add(new MoveAction(baseX + 29, baseY - 5, baseZ - 1));
        actionSequence.Add(new RightClickAction(200));
        actionSequence.Add(new MoveAction(baseX + 24, baseY - 6, baseZ - 2));

        actionSequence.Add(new HotkeyAction(VK_F5, 800)); //blanks

        actionSequence.Add(new HotkeyAction(VK_F9, 800)); //fluids

        for (int i = 0; i < 20; i++)
        {
            actionSequence.Add(new ScanBackpackAction());
        }

        actionSequence.Add(new MoveAction(baseX + 29, baseY - 6, baseZ - 2));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));
        actionSequence.Add(new MoveAction(baseX + 33, baseY + 0, baseZ - 1));
        actionSequence.Add(new MoveAction(baseX + 32, baseY + 1, baseZ - 1));
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));
        actionSequence.Add(new MoveAction(baseX + 25, baseY + 8, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 20, baseY + 13, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 13, baseY + 15, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 8, baseY + 10, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 1, baseY + 5, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 0, baseY + 3, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX - 2, baseY - 2, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 2, baseY - 4, baseZ + 0));
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

    static Thread motionDetectionThread;
    static bool motionDetectionRunning = false;
    static DateTime lastMotionTime = DateTime.MinValue;
    static DateTime lastUtaniGranHurTime = DateTime.MinValue;
    static DateTime lastUtaniGranHurAttemptTime = DateTime.MinValue;
    static DateTime lastUtanaVidTime = DateTime.MinValue;
    static DateTime lastUtanaVidAttemptTime = DateTime.MinValue;
    static Coordinate lastKnownPosition = null;
    static readonly object positionLock = new object();
    static readonly object spellCastingLock = new object();
    static bool characterIsMoving = false;
    static bool movementDetectedSinceStart = false;
    static int lastInvisibilityCode = -1;

    const int UTANI_GRAN_HUR_INTERVAL_SECONDS = 20;
    const int UTANA_VID_INTERVAL_SECONDS = 180; // 3 minutes
    const int UTANA_VID_RETRY_INTERVAL_SECONDS = 3; // Retry failed utana vid after 5 seconds
    const int UTANI_GRAN_HUR_RETRY_INTERVAL_SECONDS = 3; // Retry failed utani gran hur after 5 seconds
    const int POSITION_CHECK_INTERVAL_MS = 1000; // Check position every second
    const int MIN_SPELL_INTERVAL_SECONDS = 3; // Minimum 3 seconds between spells
    const double MIN_SPEED_FOR_UTANI_GRAN_HUR = 400.0; // Speed threshold for successful utani gran hur

    // Add this method to start the motion detection thread
    static void StartMotionDetectionThread()
    {
        ReadMemoryValues();
        if (motionDetectionThread != null && motionDetectionThread.IsAlive)
        {
            Console.WriteLine("[MOTION] Motion detection thread already running");
            return;
        }

        motionDetectionRunning = true;
        movementDetectedSinceStart = false;
        motionDetectionThread = new Thread(MotionDetectionWorker)
        {
            IsBackground = true,
            Name = "MotionDetectionThread"
        };
        motionDetectionThread.Start();
        Console.WriteLine("[MOTION] Motion detection thread started");
    }

    // Add this method to stop the motion detection thread
    static void StopMotionDetectionThread()
    {
        motionDetectionRunning = false;
        if (motionDetectionThread != null && motionDetectionThread.IsAlive)
        {
            motionDetectionThread.Join(2000);
            Console.WriteLine("[MOTION] Motion detection thread stopped");
        }
    }

    // Helper method to safely cast spells with coordination
    static bool TryCastSpell(int keyCode, string spellName, ref DateTime lastCastTime, int intervalSeconds)
    {
        lock (spellCastingLock)
        {
            DateTime now = DateTime.Now;

            if ((now - lastCastTime).TotalSeconds < intervalSeconds)
            {
                return false;
            }

            DateTime lastAnySpell = GetLatestSpellTime();
            if ((now - lastAnySpell).TotalSeconds < MIN_SPELL_INTERVAL_SECONDS)
            {
                Console.WriteLine($"[MOTION] Delaying {spellName} cast - too close to previous spell");
                return false;
            }

            Console.WriteLine($"[MOTION] Casting {spellName}");
            SendKeyPress(keyCode);
            lastCastTime = now;
            return true;
        }
    }

    // Helper method to check if utana vid was successful
    static bool CheckUtanaVidSuccess()
    {
        ReadMemoryValues();
        return invisibilityCode != 1;
    }

    // Helper method to check if utani gran hur was successful
    static bool CheckUtaniGranHurSuccess()
    {
        ReadMemoryValues();
        bool success = speed >= MIN_SPEED_FOR_UTANI_GRAN_HUR;
        Console.WriteLine($"[MOTION] Utani Gran Hur validation - Speed: {speed:F1}, Success: {success}");
        return success;
    }

    // Helper method to get the most recent spell cast time
    static DateTime GetLatestSpellTime()
    {
        DateTime latest = DateTime.MinValue;

        if (lastUtaniGranHurTime > latest)
            latest = lastUtaniGranHurTime;
        if (lastUtanaVidTime > latest)
            latest = lastUtanaVidTime;

        return latest;
    }

    static void MotionDetectionWorker()
    {
        Console.WriteLine("[MOTION] Motion detection worker started");

        try
        {
            while (motionDetectionRunning && programRunning)
            {
                try
                {
                    ReadMemoryValues();
                    Coordinate currentPosition = new Coordinate
                    {
                        X = currentX,
                        Y = currentY,
                        Z = currentZ
                    };

                    lock (positionLock)
                    {
                        if (lastKnownPosition == null)
                        {
                            lastKnownPosition = new Coordinate
                            {
                                X = currentPosition.X,
                                Y = currentPosition.Y,
                                Z = currentPosition.Z
                            };
                            Console.WriteLine($"[MOTION] Initial position set: ({currentPosition.X}, {currentPosition.Y}, {currentPosition.Z})");
                        }
                        else
                        {
                            bool hasMoved = lastKnownPosition.X != currentPosition.X ||
                                           lastKnownPosition.Y != currentPosition.Y ||
                                           lastKnownPosition.Z != currentPosition.Z;

                            if (hasMoved)
                            {
                                characterIsMoving = true;
                                lastMotionTime = DateTime.Now;

                                if (!movementDetectedSinceStart)
                                {
                                    movementDetectedSinceStart = true;
                                    Console.WriteLine("[MOTION] First movement detected since start - utana vid now eligible");
                                }

                                int distanceX = Math.Abs(currentPosition.X - lastKnownPosition.X);
                                int distanceY = Math.Abs(currentPosition.Y - lastKnownPosition.Y);
                                if (distanceX > 1 || distanceY > 1 || currentPosition.Z != lastKnownPosition.Z)
                                {
                                    Console.WriteLine($"[MOTION] Significant movement detected: ({lastKnownPosition.X}, {lastKnownPosition.Y}, {lastKnownPosition.Z}) -> ({currentPosition.X}, {currentPosition.Y}, {currentPosition.Z})");
                                }

                                lastKnownPosition.X = currentPosition.X;
                                lastKnownPosition.Y = currentPosition.Y;
                                lastKnownPosition.Z = currentPosition.Z;
                            }
                            else
                            {
                                if (characterIsMoving && (DateTime.Now - lastMotionTime).TotalSeconds > 3)
                                {
                                    characterIsMoving = false;
                                    Console.WriteLine("[MOTION] Character stopped moving");
                                }
                            }
                        }
                    }

                    DateTime now = DateTime.Now;
                    bool needsUtanaVid = invisibilityCode == 1;

                    // Check if we need to cast utana vid (F11)
                    if (movementDetectedSinceStart && needsUtanaVid)
                    {
                        bool shouldCastUtanaVid = false;

                        if ((now - lastUtanaVidTime).TotalSeconds >= UTANA_VID_INTERVAL_SECONDS)
                        {
                            shouldCastUtanaVid = true;
                        }
                        else if ((now - lastUtanaVidAttemptTime).TotalSeconds >= UTANA_VID_RETRY_INTERVAL_SECONDS &&
                                 lastUtanaVidAttemptTime > lastUtanaVidTime)
                        {
                            shouldCastUtanaVid = true;
                            Console.WriteLine("[MOTION] Retrying utana vid (previous attempt failed)");
                        }

                        if (shouldCastUtanaVid)
                        {
                            lastUtanaVidAttemptTime = now;

                            if (TryCastSpell(VK_F11, "utana vid", ref lastUtanaVidTime, 0))
                            {
                                lastUtanaVidTime = now; // Update the successful cast time

                                if (CheckUtanaVidSuccess())
                                {
                                    Console.WriteLine("[MOTION] Utana vid successful - invisibility removed");
                                }
                                else
                                {
                                    Console.WriteLine("[MOTION] Utana vid failed - still invisible, will retry");
                                    lastUtanaVidTime = lastUtanaVidAttemptTime - TimeSpan.FromSeconds(UTANA_VID_INTERVAL_SECONDS);
                                }
                            }
                        }
                    }
                    // Check if we need to cast utani gran hur (backslash)
                    else if (characterIsMoving)
                    {
                        bool shouldCastUtaniGranHur = false;

                        if ((now - lastUtaniGranHurTime).TotalSeconds >= UTANI_GRAN_HUR_INTERVAL_SECONDS)
                        {
                            shouldCastUtaniGranHur = true;
                        }
                        else if ((now - lastUtaniGranHurAttemptTime).TotalSeconds >= UTANI_GRAN_HUR_RETRY_INTERVAL_SECONDS &&
                                 lastUtaniGranHurAttemptTime > lastUtaniGranHurTime)
                        {
                            shouldCastUtaniGranHur = true;
                            Console.WriteLine("[MOTION] Retrying utani gran hur (previous attempt failed)");
                        }

                        if (currentZ != 8 && shouldCastUtaniGranHur)
                        {
                            lastUtaniGranHurAttemptTime = now;

                            if (TryCastSpell(BACKSLASH, "utani gran hur", ref lastUtaniGranHurTime, 0))
                            {
                                lastUtaniGranHurTime = now; // Update the successful cast time

                                if (CheckUtaniGranHurSuccess())
                                {
                                    Console.WriteLine("[MOTION] Utani gran hur successful - speed increased");
                                }
                                else
                                {
                                    Console.WriteLine("[MOTION] Utani gran hur failed - speed too low, will retry");
                                    lastUtaniGranHurTime = lastUtaniGranHurAttemptTime - TimeSpan.FromSeconds(UTANI_GRAN_HUR_INTERVAL_SECONDS);
                                }
                            }
                        }
                    }

                    Thread.Sleep(POSITION_CHECK_INTERVAL_MS);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MOTION] Error in motion detection loop: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MOTION] Fatal error in motion detection thread: {ex.Message}");
        }
        finally
        {
            Console.WriteLine("[MOTION] Motion detection worker stopped");
        }
    }

    // Add a method to manually reset motion detection (useful when teleporting/traveling)
    static void ResetMotionDetection()
    {
        lock (positionLock)
        {
            lastKnownPosition = null;
            characterIsMoving = false;
            lastMotionTime = DateTime.MinValue;
            movementDetectedSinceStart = false;
            Console.WriteLine("[MOTION] Motion detection reset - movement eligibility reset");
        }
    }

    // Add a method to manually reset spell timers
    static void ResetSpellTimers()
    {
        lock (spellCastingLock)
        {
            lastUtaniGranHurTime = DateTime.MinValue;
            lastUtaniGranHurAttemptTime = DateTime.MinValue;
            lastUtanaVidTime = DateTime.MinValue;
            lastUtanaVidAttemptTime = DateTime.MinValue;
            Console.WriteLine("[MOTION] Spell timers reset");
        }
    }

    // Add a method to check if character is currently moving
    static bool IsCharacterMoving()
    {
        lock (positionLock)
        {
            return characterIsMoving;
        }
    }


    // Add a method to get spell status information
    static void PrintSpellStatus()
    {
        lock (spellCastingLock)
        {
            DateTime now = DateTime.Now;
            double utaniGranHurCooldown = UTANI_GRAN_HUR_INTERVAL_SECONDS - (now - lastUtaniGranHurTime).TotalSeconds;
            double utanaVidCooldown = UTANA_VID_INTERVAL_SECONDS - (now - lastUtanaVidTime).TotalSeconds;
            double utanaVidRetryCooldown = UTANA_VID_RETRY_INTERVAL_SECONDS - (now - lastUtanaVidAttemptTime).TotalSeconds;
            double utaniGranHurRetryCooldown = UTANI_GRAN_HUR_RETRY_INTERVAL_SECONDS - (now - lastUtaniGranHurAttemptTime).TotalSeconds;

            Console.WriteLine("[MOTION] Spell Status:");

            // Utani Gran Hur status
            if (lastUtaniGranHurAttemptTime > lastUtaniGranHurTime && speed < MIN_SPEED_FOR_UTANI_GRAN_HUR)
            {
                Console.WriteLine($"  Utani Gran Hur: {(utaniGranHurRetryCooldown > 0 ? $"{utaniGranHurRetryCooldown:F1}s retry cooldown" : "Ready to retry")} (last attempt failed)");
            }
            else
            {
                Console.WriteLine($"  Utani Gran Hur: {(utaniGranHurCooldown > 0 ? $"{utaniGranHurCooldown:F1}s cooldown" : "Ready")}");
            }

            // Utana Vid status
            if (movementDetectedSinceStart)
            {
                if (lastUtanaVidAttemptTime > lastUtanaVidTime && invisibilityCode == 1)
                {
                    Console.WriteLine($"  Utana Vid: {(utanaVidRetryCooldown > 0 ? $"{utanaVidRetryCooldown:F1}s retry cooldown" : "Ready to retry")} (last attempt failed)");
                }
                else
                {
                    Console.WriteLine($"  Utana Vid: {(utanaVidCooldown > 0 ? $"{utanaVidCooldown:F1}s cooldown" : "Ready")}");
                }
            }
            else
            {
                Console.WriteLine("  Utana Vid: Waiting for movement before becoming eligible");
            }

            Console.WriteLine($"  Character moving: {IsCharacterMoving()}");
            Console.WriteLine($"  Movement detected since start: {movementDetectedSinceStart}");
            Console.WriteLine($"  Current speed: {speed:F1}");
            Console.WriteLine($"  Invisibility code: {invisibilityCode}");
        }
    }

    // Modify your Main method to start the motion detection thread
    // Add this line after you find the window handle and before the main loop:
    // StartMotionDetectionThread();

    // Also modify the quit section in your Main method:
    // Before Environment.Exit(0), add:
    // StopMotionDetectionThread();

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
        Console.WriteLine($"InvisibilityCode: {invisibilityCode}");
        Console.WriteLine($"Speed: {speed}");
        Console.WriteLine("\nControls:");
        Console.WriteLine("Q - Quit");
        Console.WriteLine("E - Drag items from backpack to ground (8x)");
        Console.WriteLine("R - Drag items from ground to backpack (8x)");
        Console.WriteLine("P - Execute action sequence");
        Console.WriteLine("M - Reset motion detection");
        Console.WriteLine("N - Check if character is moving");
        Console.WriteLine("T - Reset spell timers");
        Console.WriteLine("S - Show spell status");
        Console.WriteLine("\nAuto-spells:");
        Console.WriteLine("- Utani Gran Hur (\\): Every 20 seconds when moving");
        Console.WriteLine("- Utana Vid (F11): Every 3 minutes when invisible");
        Console.WriteLine("- Minimum 3 seconds between any spells");

        StartMotionDetectionThread();
        StartSoulPositionMonitorThread();

        while (programRunning)
        {
            ReadMemoryValues();
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
                        StopMotionDetectionThread(); // Add this line
                        StopSoulPositionMonitorThread();  // Add this line
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
                    else if (key == ConsoleKey.S)
                    {
                        PrintSpellStatus();
                    }
                    else if (key == ConsoleKey.M)
                    {
                        Console.WriteLine("Resetting motion detection...");
                        ResetMotionDetection();
                    }
                    else if (key == ConsoleKey.N)
                    {
                        bool moving = IsCharacterMoving();
                        Console.WriteLine($"Character is currently moving: {moving}");
                    }
                    else if (key == ConsoleKey.T)
                    {
                        Console.WriteLine("Resetting spell timers...");
                        ResetSpellTimers();
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
                else if (curMana >= MANA_THRESHOLD)
                {
                    if ((DateTime.Now - lastManaAction).TotalMilliseconds >= 2000)
                    {
                        SendKeyPress(VK_F2);
                        lastManaAction = DateTime.Now;
                        Console.WriteLine($"Mana>900 ({curMana:F0}), Soul: ({curSoul:F1}) - pressed F2");
                    }
                }
                if(currentZ == 8)
                {
                    tarantulaSeqeunce();
                    ExecuteActionSequence();
                }
                if (curSoul >= 96 && curSoul <= 101)
                {
                    InitializeMiddleSequence();
                    ExecuteActionSequence();
                    SendKeyPress(VK_F2);
                    lastManaAction = DateTime.Now;
                    Console.WriteLine($"Mana>900 ({curMana:F0}), Soul>{SOUL_THRESHOLD} ({curSoul:F1}) - pressed F2");
                    SendKeyPress(VK_F2);
                    lastManaAction = DateTime.Now;
                    Console.WriteLine($"Mana>900 ({curMana:F0}), Soul>{SOUL_THRESHOLD} ({curSoul:F1}) - pressed F2");
                }
                else if (curMana >= MANA_THRESHOLD && curSoul >= 0 && curSoul <= 4)
                {
                    InitializeActionSequence();
                    ExecuteActionSequence();
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

        targetId = ReadInt32(TARGET_ID_OFFSET);
        invisibilityCode = ReadIntFromPointerOffset(INVIS_OFFSET);
        speed = ReadIntFromPointerOffset(SPEED_OFFSET);
    }


    static int ReadIntFromPointerOffset(int offset)
    {
        IntPtr address = IntPtr.Add(moduleBase, (int)BASE_ADDRESS);
        byte[] buffer = new byte[4];

        if (ReadProcessMemory(processHandle, address, buffer, 4, out _))
        {
            IntPtr finalAddress = BitConverter.ToInt32(buffer, 0);
            finalAddress = IntPtr.Add(finalAddress, offset);

            byte[] valueBuffer = new byte[4];
            if (ReadProcessMemory(processHandle, finalAddress, valueBuffer, 4, out _))
            {
                return BitConverter.ToInt32(valueBuffer, 0);
            }
        }
        return 0;
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
    public class FightTarantulasAction : Action
    {
        private CoordinateData loadedCoords;
        private static int currentCoordIndex = 0;
        private static bool returningToStart = false;
        private static string cordsFilePath = "cords.json";
        private static int totalCoords = 0;
        private static bool boolInit = true;
        static private int globalLastIndex = -1;
        private static int maxBacktrackDistance = 10;
        private static TimeSpan currentTargetEngagementDuration = TimeSpan.Zero;
        private static DateTime targetEngagementStartTime = DateTime.MinValue;
        private static int currentTargetEngagementId = 0;
        private static bool shouldForceTargetChange = false;

        public override string GetDescription()
        {
            return "Fight tarantulas";
        }
        public FightTarantulasAction()
        {
            MaxRetries = 1;
        }

        public override bool Execute()
        {
            PlayCoordinatesIteration();
            SendKeyPress(VK_ESCAPE);
            SendKeyPress(VK_ESCAPE);
            SendKeyPress(VK_ESCAPE);
            ReturnToFirstWaypoint();
            return true;
        }

        private void ReturnToFirstWaypoint()
        {
            try
            {
                // 1. Recognize where we are by reading current memory values
                ReadMemoryValues();
                Console.WriteLine($"[RETURN] Current position: ({currentX}, {currentY}, {currentZ})");

                // Make sure coords are loaded
                if (loadedCoords == null)
                {
                    string json = File.ReadAllText(cordsFilePath);
                    loadedCoords = JsonSerializer.Deserialize<CoordinateData>(json);

                    if (loadedCoords == null || loadedCoords.cords.Count == 0)
                    {
                        Console.WriteLine("[RETURN] ERROR: No waypoints loaded");
                        return;
                    }
                }

                List<Coordinate> waypoints = loadedCoords.cords;

                // 3. Get coordinates of [0] waypoint (first waypoint)
                if (waypoints == null || waypoints.Count == 0)
                {
                    Console.WriteLine("[RETURN] ERROR: No waypoints available");
                    return;
                }

                Coordinate firstWaypoint = waypoints[0];
                Console.WriteLine($"[RETURN] Target waypoint [0]: ({firstWaypoint.X}, {firstWaypoint.Y}, {firstWaypoint.Z})");

                // Check if we're already at the first waypoint
                if (IsAtPosition(firstWaypoint.X, firstWaypoint.Y, firstWaypoint.Z))
                {
                    Console.WriteLine("[RETURN] Already at first waypoint [0]");
                    currentCoordIndex = 0;
                    globalLastIndex = 0;
                    return;
                }

                // 2. Find closest waypoint to current position
                currentCoordIndex = FindClosestWaypointIndex(waypoints, currentX, currentY, currentZ);
                Console.WriteLine($"[RETURN] Closest waypoint to current position: [{currentCoordIndex}]");

                // Analyze both paths and choose the optimal one
                PathAnalysisResult pathAnalysis = AnalyzePaths(waypoints, currentCoordIndex);
                List<int> optimalPath = pathAnalysis.UseReversePath ? pathAnalysis.ReversePath : pathAnalysis.ForwardPath;
                string direction = pathAnalysis.UseReversePath ? "REVERSE" : "FORWARD";

                Console.WriteLine($"[RETURN] Using {direction} path ({optimalPath.Count} waypoints)");
                Console.WriteLine($"[RETURN] Forward path: {pathAnalysis.ForwardPath.Count} waypoints, Reverse path: {pathAnalysis.ReversePath.Count} waypoints");

                // 4. Execute path using furthest reachable waypoint logic
                DateTime returnStartTime = DateTime.Now;
                const int RETURN_TIMEOUT_MINUTES = 3;
                int pathProgress = 0; // Index in our optimal path

                while (!IsAtPosition(firstWaypoint.X, firstWaypoint.Y, firstWaypoint.Z))
                {
                    // Safety timeout check
                    if ((DateTime.Now - returnStartTime).TotalMinutes > RETURN_TIMEOUT_MINUTES)
                    {
                        Console.WriteLine($"[RETURN] TIMEOUT: Failed to reach first waypoint within {RETURN_TIMEOUT_MINUTES} minutes");
                        return;
                    }

                    // Re-read position
                    ReadMemoryValues();

                    //// Handle invisibility
                    //if (invisibilityCode == 1)
                    //{
                    //    SendKeyPress(VK_F11);
                    //}

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

                    // Find furthest reachable waypoint along our optimal path
                    Coordinate nextTarget = FindFurthestReachableInPath(waypoints, optimalPath, pathProgress, firstWaypoint);

                    if (nextTarget == null)
                    {
                        Console.WriteLine("[RETURN] ERROR: Could not find next target in path");
                        break;
                    }

                    // Skip if we're already at the target
                    if (IsAtPosition(nextTarget.X, nextTarget.Y, nextTarget.Z))
                    {
                        Console.WriteLine("[RETURN] Already at target position");
                        pathProgress++;
                        continue;
                    }

                    Console.WriteLine($"[RETURN] Moving to: ({nextTarget.X}, {nextTarget.Y}, {nextTarget.Z})");

                    // Check if we need to handle Z-level change
                    if (nextTarget.Z != currentZ)
                    {
                        if (nextTarget.Z > currentZ)
                        {
                            Console.WriteLine("[RETURN] Need to go UP");
                            RightClickOnCharacter();
                        }
                        else
                        {
                            Console.WriteLine("[RETURN] Need to go DOWN");
                            SendKeyPress(VK_DOWN);
                        }
                    }
                    else
                    {
                        // Same Z level - click on target
                        ClickWaypoint(nextTarget.X, nextTarget.Y);
                    }

                    // Update progress in path
                    pathProgress = UpdatePathProgress(waypoints, optimalPath, pathProgress);
                }

                Console.WriteLine("[RETURN] Successfully reached first waypoint [0]");
                currentCoordIndex = 0;
                globalLastIndex = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RETURN] ERROR: {ex.Message}");
                Console.WriteLine($"[RETURN] Stack trace: {ex.StackTrace}");
            }
        }

        private class PathAnalysisResult
        {
            public List<int> ForwardPath { get; set; }
            public List<int> ReversePath { get; set; }
            public bool UseReversePath { get; set; }
        }

        private PathAnalysisResult AnalyzePaths(List<Coordinate> waypoints, int currentIndex)
        {
            // Calculate forward path (current -> 0)
            List<int> forwardPath = new List<int>();
            if (currentIndex > 0)
            {
                for (int i = currentIndex - 1; i >= 0; i--)
                {
                    forwardPath.Add(i);
                }
            }

            // Calculate reverse path (current -> end -> 0)
            List<int> reversePath = new List<int>();
            // First go from current to end
            for (int i = currentIndex + 1; i < waypoints.Count; i++)
            {
                reversePath.Add(i);
            }
            // Then wrap around from end to 0
            for (int i = waypoints.Count - 1; i >= 0; i--)
            {
                reversePath.Add(i);
            }

            // Count unique waypoints (remove duplicates for circular paths)
            HashSet<int> forwardSet = new HashSet<int>(forwardPath);
            HashSet<int> reverseSet = new HashSet<int>(reversePath);

            bool useReverse = reverseSet.Count < forwardSet.Count &&
                              Math.Abs(reverseSet.Count - forwardSet.Count) >= 2;

            Console.WriteLine($"[PATH] Forward path: {forwardSet.Count} unique waypoints");
            Console.WriteLine($"[PATH] Reverse path: {reverseSet.Count} unique waypoints");

            return new PathAnalysisResult
            {
                ForwardPath = forwardPath,
                ReversePath = reversePath,
                UseReversePath = useReverse
            };
        }

        private Coordinate FindFurthestReachableInPath(List<Coordinate> waypoints, List<int> path, int currentProgress, Coordinate finalTarget)
        {
            const int MAX_DISTANCE = 5;
            ReadMemoryValues();

            // Check if we can reach the final target directly
            int distanceToFinal = Math.Abs(finalTarget.X - currentX) + Math.Abs(finalTarget.Y - currentY);
            if (distanceToFinal <= MAX_DISTANCE && finalTarget.Z == currentZ)
            {
                Console.WriteLine($"[RETURN] Can reach final target directly (distance: {distanceToFinal})");
                return finalTarget;
            }

            // Find furthest reachable waypoint in our path
            Coordinate furthestReachable = null;
            int furthestDistance = 0;
            int furthestProgress = currentProgress;

            // Look ahead in our path
            for (int i = currentProgress; i < Math.Min(currentProgress + 10, path.Count); i++)
            {
                int waypointIndex = path[i];
                if (waypointIndex < 0 || waypointIndex >= waypoints.Count) continue;

                Coordinate candidate = waypoints[waypointIndex];

                // Skip different Z levels for now
                if (candidate.Z != currentZ) continue;

                // Check if reachable
                int distanceX = Math.Abs(candidate.X - currentX);
                int distanceY = Math.Abs(candidate.Y - currentY);
                int totalDistance = distanceX + distanceY;

                if (distanceX <= MAX_DISTANCE && distanceY <= MAX_DISTANCE)
                {
                    // Prefer further waypoints for efficiency
                    if (totalDistance >= furthestDistance)
                    {
                        furthestReachable = candidate;
                        furthestDistance = totalDistance;
                        furthestProgress = i;
                    }
                }
            }

            // If no waypoint in path is reachable, use directional movement
            if (furthestReachable == null)
            {
                Console.WriteLine("[RETURN] No waypoint in path reachable, using directional movement");
                return CalculateDirectionalMove(currentX, currentY, currentZ, finalTarget);
            }

            Console.WriteLine($"[RETURN] Found furthest reachable waypoint at distance {furthestDistance}");
            return furthestReachable;
        }

        private int UpdatePathProgress(List<Coordinate> waypoints, List<int> path, int currentProgress)
        {
            ReadMemoryValues();

            // Find how far we've progressed in our path
            for (int i = currentProgress; i < path.Count; i++)
            {
                int waypointIndex = path[i];
                if (waypointIndex < 0 || waypointIndex >= waypoints.Count) continue;

                Coordinate waypoint = waypoints[waypointIndex];

                // Check if we've reached or passed this waypoint
                int distance = Math.Abs(waypoint.X - currentX) + Math.Abs(waypoint.Y - currentY);
                if (distance <= 2 && waypoint.Z == currentZ)
                {
                    Console.WriteLine($"[RETURN] Reached waypoint [{waypointIndex}] in path (progress: {i})");
                    return i + 1; // Move to next waypoint in path
                }
            }

            return currentProgress; // No progress made
        }

        private Coordinate CalculateDirectionalMove(int currentX, int currentY, int currentZ, Coordinate target)
        {
            // Calculate direction to target
            int deltaX = target.X - currentX;
            int deltaY = target.Y - currentY;

            // Normalize to maximum 3 squares in each direction
            int stepX = 0;
            int stepY = 0;

            if (deltaX != 0)
            {
                stepX = Math.Sign(deltaX) * Math.Min(3, Math.Abs(deltaX));
            }

            if (deltaY != 0)
            {
                stepY = Math.Sign(deltaY) * Math.Min(3, Math.Abs(deltaY));
            }

            return new Coordinate
            {
                X = currentX + stepX,
                Y = currentY + stepY,
                Z = currentZ
            };
        }












        static DateTime returnStartTime = DateTime.MinValue;
        private void PlayCoordinatesIteration()
        {

            if (loadedCoords == null)
            {
                string json = File.ReadAllText(cordsFilePath);
                loadedCoords = JsonSerializer.Deserialize<CoordinateData>(json);

                if (loadedCoords == null || loadedCoords.cords.Count == 0)
                {
                    Console.WriteLine("[FIGHT] No waypoints loaded");
                    return;
                }

                totalCoords = loadedCoords.cords.Count;
            }

            List<Coordinate> waypoints = loadedCoords.cords;

            while (true)
            {
                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                    if (keyInfo.Key == ConsoleKey.X)
                    {
                        Console.WriteLine("\n[STOP] 'X' key pressed. Stopping iteration...");
                        return;
                    }
                }

                ReadMemoryValues();
                //if(invisibilityCode == 1)
                //{
                //    SendKeyPress(VK_F11);
                //    Thread.Sleep(1000);
                //}

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

                if (curSoul >= 200)
                {
                    return;
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

                ReadMemoryValues();

                if (targetId == 0)
                {
                    SendKeyPress(VK_F6);
                    Thread.Sleep(150);
                }

                ReadMemoryValues();
                // If combat is active, handle combat
                if (targetId != 0)
                {
                    while (true)
                    {
                        if (Console.KeyAvailable)
                        {
                            ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                            if (keyInfo.Key == ConsoleKey.X)
                            {
                                Console.WriteLine("\n[STOP] 'X' key pressed during combat. Stopping iteration...");
                                return;
                            }
                        }

                        ReadMemoryValues();
                        //if (invisibilityCode == 1){
                        //    SendKeyPress(VK_F11);
                        //}

                        hpPercent = (curHP / maxHP) * 100;
                        manaPercent = (curMana / maxMana) * 100;
                        mana = curMana;

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

                        if (targetId != 0)
                        {
                            Console.WriteLine($"[COMBAT] Fighting target {targetId}...");
                            Thread.Sleep(1000);
                            ReadMemoryValues();

                            ReadMemoryValues();
                            if (targetId == 0)
                            {
                                Console.WriteLine("[COMBAT] Target killed!");
                                break;
                            }
                        }
                    }
                }
                NavigationAction action = DetermineNextAction(waypoints, currentX, currentY, currentZ);

                switch (action.Type)
                {
                    case ActionType.ClickWaypoint:
                        if (ClickWaypoint(action.TargetX, action.TargetY))
                        {
                            WaitForMovementCompletion(action.TargetX, action.TargetY);
                        }
                        currentCoordIndex = action.WaypointIndex;
                        globalLastIndex = currentCoordIndex;
                        break;

                    case ActionType.KeyboardStep:  // NEW CASE
                        Console.WriteLine($"[PLAY] Executing keyboard step: {action.Direction}");

                        // Send the appropriate arrow key
                        switch (action.Direction)
                        {
                            case ArrowAction.ArrowDirection.Left:
                                SendKeyPress(VK_LEFT);
                                break;
                            case ArrowAction.ArrowDirection.Right:
                                SendKeyPress(VK_RIGHT);
                                break;
                            case ArrowAction.ArrowDirection.Up:
                                SendKeyPress(VK_UP);
                                break;
                            case ArrowAction.ArrowDirection.Down:
                                SendKeyPress(VK_DOWN);
                                break;
                        }

                        // Wait for movement
                        Thread.Sleep(200);

                        // Check if we've made progress
                        ReadMemoryValues();
                        int newDistance = Math.Abs(action.TargetX - currentX) + Math.Abs(action.TargetY - currentY);

                        if (newDistance == 0)
                        {
                            Console.WriteLine("[PLAY] Reached keyboard target!");
                            currentCoordIndex = action.WaypointIndex;
                            globalLastIndex = currentCoordIndex;
                        }

                        // Recovery mode tracking
                        if (isInRecoveryMode)
                        {
                            recoveryAttempts++;
                            if (recoveryAttempts > 20)
                            {
                                Console.WriteLine("[PLAY] Too many recovery attempts - forcing exit of recovery mode");
                                isInRecoveryMode = false;
                                recoveryAttempts = 0;
                            }
                        }
                        break;

                    case ActionType.WaitForTarget:
                        SendKeyPress(VK_F6);
                        Thread.Sleep(100);
                        break;
                }

                Thread.Sleep(100);
            }


        }

        static void WaitForMovementCompletion(int targetX, int targetY)
        {
            DateTime startTime = DateTime.Now;
            const int TIMEOUT_MS = 2000;

            // Movement tracking variables
            int lastPosX = -1;
            int lastPosY = -1;
            int noMovementCount = 0;
            const int MAX_NO_MOVEMENT_COUNT = 1;

            while ((DateTime.Now - startTime).TotalMilliseconds < TIMEOUT_MS)
            {
                Thread.Sleep(500);
                SendKeyPress(VK_F6);

                ReadMemoryValues();
                {
                    // Check if monster appeared
                    if (targetId != 0)
                    {
                        return;
                    }

                    // Check if character moved
                    if (lastPosX != -1 && lastPosY != -1)
                    {
                        if (currentX == lastPosX && currentY == lastPosY)
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
                            Console.WriteLine($"[MOVE] Character moved from ({lastPosX},{lastPosY}) to ({currentX},{currentY})");
                        }
                    }

                    // Update last known position
                    lastPosX = currentX;
                    lastPosY = currentY;

                    // Check if we've reached the target
                    int distanceX = Math.Abs(targetX - currentX);
                    int distanceY = Math.Abs(targetY - currentY);
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

        public class NavigationAction
        {
            public ActionType Type { get; set; }
            public int TargetX { get; set; }
            public int TargetY { get; set; }
            public int FromZ { get; set; }
            public int ToZ { get; set; }
            public int WaypointIndex { get; set; }
            public ArrowAction.ArrowDirection Direction { get; set; }  // Add this field
        }

        public enum ActionType
        {
            UseKeyboard,    // Walk with arrow keys
            ClickWaypoint,  // Click on game screen
            UseF4,          // Press F4 for stairs
            WaitAtPosition,
            WaitForTarget,
            KeyboardStep    // NEW: Single keyboard step
        }

        static int FindWaypointIndex(List<Coordinate> waypoints, Coordinate target)
        {
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (waypoints[i].X == target.X && waypoints[i].Y == target.Y && waypoints[i].Z == target.Z)
                    return i;
            }
            return currentCoordIndex;
        }

        static Coordinate CreateRecoveryStep(int currentX, int currentY, int currentZ, Coordinate target)
        {
            // Calculate direction to target
            int deltaX = target.X - currentX;
            int deltaY = target.Y - currentY;

            Console.WriteLine($"[RECOVERY] Creating step from ({currentX},{currentY}) toward ({target.X},{target.Y})");
            Console.WriteLine($"[RECOVERY] Delta: X={deltaX}, Y={deltaY}");

            // Always move only 1 square at a time for keyboard navigation
            int stepX = 0;
            int stepY = 0;

            // Choose direction based on larger distance, or randomly if equal
            if (Math.Abs(deltaX) > Math.Abs(deltaY))
            {
                // X distance is larger - move in X direction
                stepX = Math.Sign(deltaX);
            }
            else if (Math.Abs(deltaY) > Math.Abs(deltaX))
            {
                // Y distance is larger - move in Y direction
                stepY = Math.Sign(deltaY);
            }
            else if (deltaX != 0)
            {
                // Equal distances and both non-zero - randomly choose
                Random rand = new Random();
                if (rand.Next(2) == 0)
                {
                    stepX = Math.Sign(deltaX);
                }
                else
                {
                    stepY = Math.Sign(deltaY);
                }
            }
            else if (deltaY != 0)
            {
                // Only Y movement needed
                stepY = Math.Sign(deltaY);
            }
            else
            {
                // Already at target (shouldn't happen)
                Console.WriteLine("[RECOVERY] Already at target!");
                return new Coordinate { X = currentX, Y = currentY, Z = currentZ };
            }

            Coordinate recoveryStep = new Coordinate
            {
                X = currentX + stepX,
                Y = currentY + stepY,
                Z = currentZ
            };

            Console.WriteLine($"[RECOVERY] Created step: ({recoveryStep.X},{recoveryStep.Y},{recoveryStep.Z})");
            Console.WriteLine($"[RECOVERY] Step size: X={stepX}, Y={stepY}");

            return recoveryStep;
        }


        // Add the recovery system variables
        static bool isInRecoveryMode = false;
        static Coordinate recoveryTarget = null;
        static int recoveryAttempts = 0;
        static DateTime lastRecoveryTime = DateTime.MinValue;

        static int lastTargetedIndex = -1;
        static Coordinate lastTargetedWaypoint = null;

        // Constants
        const int LOST_THRESHOLD = 15; // If we're more than 15 squares from any waypoint

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

            // Keep track of the best reachable waypoints found (for random selection)
            List<(Coordinate waypoint, int index, int distance)> reachableWaypoints = new List<(Coordinate, int, int)>();

            // Check for reachable waypoints - look forward through the waypoint list
            for (int i = 1; i < Math.Min(15, waypoints.Count); i++) // Start from i=1 to skip current waypoint
            {
                // Calculate next index, wrapping around if needed
                int checkIndex = (currentCoordIndex + i) % waypoints.Count;
                Coordinate check = waypoints[checkIndex];
                Console.WriteLine($"[NAV] Checking waypoint {checkIndex}: ({check.X},{check.Y},{check.Z})");

                // Handle Z-level changes for the very next waypoint (i == 1)
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
                    // This waypoint is reachable, add it to our list
                    reachableWaypoints.Add((check, checkIndex, totalDistance));
                    Console.WriteLine($"[NAV] Reachable waypoint added: {checkIndex} at distance {totalDistance}");
                }
            }

            // Process reachable waypoints for random selection
            Coordinate selectedWaypoint = null;
            int selectedIndex = -1;
            int selectedDistance = 0;

            if (reachableWaypoints.Count > 0)
            {
                // Sort by distance (descending) to get the furthest waypoints first
                reachableWaypoints.Sort((a, b) => b.distance.CompareTo(a.distance));

                // Get top 3 (or as many as we have)
                int topCount = Math.Min(3, reachableWaypoints.Count);
                var topWaypoints = reachableWaypoints.Take(topCount).ToList();

                // Check if ALL top waypoints have the same Z as current position
                bool allTopSameZ = topWaypoints.All(wp => wp.waypoint.Z == currentZ);

                // Check if the next 10 waypoints also have the same Z
                bool next10SameZ = true;
                for (int i = 1; i <= 10 && i < waypoints.Count; i++)
                {
                    int checkIndex = (currentCoordIndex + i) % waypoints.Count;
                    if (waypoints[checkIndex].Z != currentZ)
                    {
                        next10SameZ = false;
                        break;
                    }
                }

                Console.WriteLine($"[NAV] Z-level checks: All top waypoints same Z={allTopSameZ}, Next 10 same Z={next10SameZ}");

                if (allTopSameZ && next10SameZ)
                {
                    // Randomly select from the top waypoints
                    Random rand = new Random();
                    int selectedIdx = rand.Next(topCount);
                    var selected = topWaypoints[selectedIdx];

                    selectedWaypoint = selected.waypoint;
                    selectedIndex = selected.index;
                    selectedDistance = selected.distance;

                    Console.WriteLine($"[NAV] Random selection enabled - Selected waypoint {selectedIndex} from top {topCount} reachable waypoints (distance={selectedDistance})");
                    Console.WriteLine($"[NAV] Available top waypoints were:");
                    for (int i = 0; i < topWaypoints.Count; i++)
                    {
                        var wp = topWaypoints[i];
                        Console.WriteLine($"[NAV]   {i}: Index {wp.index}, distance {wp.distance}");
                    }
                }
                else
                {
                    // Take the furthest waypoint (first in sorted list)
                    var selected = topWaypoints[0];
                    selectedWaypoint = selected.waypoint;
                    selectedIndex = selected.index;
                    selectedDistance = selected.distance;

                    Console.WriteLine($"[NAV] Z-level criteria not met - Selected furthest waypoint {selectedIndex} (distance={selectedDistance})");
                    if (!allTopSameZ)
                        Console.WriteLine($"[NAV] Reason: Not all top waypoints have same Z-level");
                    if (!next10SameZ)
                        Console.WriteLine($"[NAV] Reason: Next 10 waypoints don't all have same Z-level");
                }
            }

            // If still no waypoint found, create a directional step
            if (selectedWaypoint == null)
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
                selectedWaypoint = new Coordinate
                {
                    X = currentX + stepX,
                    Y = currentY + stepY,
                    Z = currentZ
                };

                selectedIndex = -1;
                selectedDistance = 1;

                Console.WriteLine($"[NAV] Returning directional step: ({selectedWaypoint.X},{selectedWaypoint.Y},{selectedWaypoint.Z}) toward waypoint {nextIndex}");
            }

            // Remember what we're targeting
            if (selectedIndex != currentCoordIndex)
            {
                lastTargetedIndex = selectedIndex;
                lastTargetedWaypoint = selectedWaypoint;
                Console.WriteLine($"[NAV] Targeting waypoint {selectedIndex} at ({selectedWaypoint.X},{selectedWaypoint.Y},{selectedWaypoint.Z})");
            }

            Console.WriteLine($"[NAV] Selected reachable waypoint: {selectedIndex} at ({selectedWaypoint.X},{selectedWaypoint.Y},{selectedWaypoint.Z}) distance={selectedDistance}");
            return selectedWaypoint;
        }

        static NavigationAction DetermineNextAction(List<Coordinate> waypoints, int currentX, int currentY, int currentZ)
        {
            Console.WriteLine($"[NAV] DetermineNextAction - Current position: ({currentX}, {currentY}, {currentZ})");
            Console.WriteLine($"[NAV] Current waypoint index: {currentCoordIndex}, Global last index: {globalLastIndex}");

            // Check if we're far from any waypoint on the same Z-level (lost condition)
            int minDistanceToAnyWaypoint = int.MaxValue;
            Coordinate closestWaypoint = null;
            int closestWaypointIndex = -1;

            for (int i = 0; i < waypoints.Count; i++)
            {
                var waypoint = waypoints[i];

                // Only consider waypoints on the same Z-level
                if (waypoint.Z != currentZ) continue;

                int distance = Math.Abs(waypoint.X - currentX) + Math.Abs(waypoint.Y - currentY);
                if (distance < minDistanceToAnyWaypoint)
                {
                    minDistanceToAnyWaypoint = distance;
                    closestWaypoint = waypoint;
                    closestWaypointIndex = i;
                }
            }

            const int LOST_THRESHOLD = 15; // If we're more than 15 squares from any waypoint
            bool wasLost = minDistanceToAnyWaypoint > LOST_THRESHOLD;

            Console.WriteLine($"[NAV] Min distance to any waypoint: {minDistanceToAnyWaypoint}");
            Console.WriteLine($"[NAV] Was lost: {wasLost}, Is in recovery mode: {isInRecoveryMode}");

            // Handle recovery mode transitions
            if (wasLost && !isInRecoveryMode)
            {
                Console.WriteLine($"[NAV] CHARACTER IS LOST - entering recovery mode. Distance to nearest: {minDistanceToAnyWaypoint}");
                isInRecoveryMode = true;
                recoveryTarget = closestWaypoint;
                recoveryAttempts = 0;
                lastRecoveryTime = DateTime.Now;
            }
            else if (!wasLost && isInRecoveryMode)
            {
                Console.WriteLine("[NAV] Character recovered - exiting recovery mode");
                isInRecoveryMode = false;
                recoveryTarget = null;
                recoveryAttempts = 0;
            }

            // Find the target waypoint using the existing logic
            Coordinate target = FindFurthestReachableWaypoint(waypoints, currentX, currentY, currentZ);

            // Check if we're already close enough to the target
            int distanceX = Math.Abs(target.X - currentX);
            int distanceY = Math.Abs(target.Y - currentY);
            int totalDistance = distanceX + distanceY;

            Console.WriteLine($"[NAV] Target: ({target.X}, {target.Y}, {target.Z})");
            Console.WriteLine($"[NAV] Distance to target: X={distanceX}, Y={distanceY}, Total={totalDistance}");

            if (distanceX == 0 && distanceY == 0)
            {
                Console.WriteLine($"[NAV] Already at target position");
                return new NavigationAction
                {
                    Type = ActionType.WaitAtPosition,
                    TargetX = currentX,
                    TargetY = currentY,
                    WaypointIndex = currentCoordIndex
                };
            }

            // Special handling for recovery mode
            if (isInRecoveryMode)
            {
                Console.WriteLine("[NAV] In recovery mode - analyzing movement options");

                // Check if we can reach any waypoint now
                bool canReachWaypoint = false;
                for (int i = 0; i < waypoints.Count; i++)
                {
                    var waypoint = waypoints[i];
                    if (waypoint.Z != currentZ) continue;

                    int dx = Math.Abs(waypoint.X - currentX);
                    int dy = Math.Abs(waypoint.Y - currentY);

                    if (dx <= 5 && dy <= 5) // Within click range
                    {
                        canReachWaypoint = true;
                        Console.WriteLine($"[NAV] Can now reach waypoint {i} - exiting recovery mode early");
                        isInRecoveryMode = false;
                        recoveryTarget = null;
                        recoveryAttempts = 0;
                        break;
                    }
                }

                // If still in recovery mode, create a step toward the recovery target
                if (isInRecoveryMode && recoveryTarget != null)
                {
                    Coordinate stepTarget = CreateRecoveryStep(currentX, currentY, currentZ, recoveryTarget);

                    // Recalculate distance to step target
                    int stepDistanceX = Math.Abs(stepTarget.X - currentX);
                    int stepDistanceY = Math.Abs(stepTarget.Y - currentY);

                    Console.WriteLine($"[NAV] Recovery step to: ({stepTarget.X}, {stepTarget.Y}, {stepTarget.Z})");

                    // Determine keyboard direction for the step
                    ArrowAction.ArrowDirection direction;

                    if (stepDistanceX > 0)
                        direction = ArrowAction.ArrowDirection.Right;
                    else if (stepDistanceX < 0)
                        direction = ArrowAction.ArrowDirection.Left;
                    else if (stepDistanceY > 0)
                        direction = ArrowAction.ArrowDirection.Down;
                    else
                        direction = ArrowAction.ArrowDirection.Up;

                    Console.WriteLine($"[NAV] Using keyboard direction: {direction}");

                    recoveryAttempts++;

                    return new NavigationAction
                    {
                        Type = ActionType.KeyboardStep,
                        TargetX = stepTarget.X,
                        TargetY = stepTarget.Y,
                        Direction = direction,
                        WaypointIndex = -1 // No specific waypoint index for recovery steps
                    };
                }
            }

            // Normal navigation logic
            // Check if we need to handle Z-level changes
            if (target.Z != currentZ)
            {
                Console.WriteLine($"[NAV] Z-level change needed: {currentZ} -> {target.Z}");

                if (target.Z > currentZ)
                {
                    Console.WriteLine("[NAV] Need to go UP - using right-click");
                    return new NavigationAction
                    {
                        Type = ActionType.UseF4,
                        TargetX = currentX,
                        TargetY = currentY,
                        FromZ = currentZ,
                        ToZ = target.Z,
                        WaypointIndex = FindWaypointIndex(waypoints, target)
                    };
                }
                else
                {
                    Console.WriteLine("[NAV] Need to go DOWN - using arrow key");
                    return new NavigationAction
                    {
                        Type = ActionType.KeyboardStep,
                        TargetX = currentX,
                        TargetY = currentY,
                        Direction = ArrowAction.ArrowDirection.Down,
                        WaypointIndex = FindWaypointIndex(waypoints, target)
                    };
                }
            }

            // Same Z-level movement decisions
            // Use keyboard for very close targets or when specifically needed
            if (totalDistance == 1)
            {
                Console.WriteLine($"[NAV] Very close target - using keyboard (distance={totalDistance})");

                // Determine keyboard direction
                ArrowAction.ArrowDirection direction;

                if (distanceX > 0)
                    direction = ArrowAction.ArrowDirection.Right;
                else if (distanceX < 0)
                    direction = ArrowAction.ArrowDirection.Left;
                else if (distanceY > 0)
                    direction = ArrowAction.ArrowDirection.Down;
                else
                    direction = ArrowAction.ArrowDirection.Up;

                Console.WriteLine($"[NAV] Using keyboard direction: {direction}");

                return new NavigationAction
                {
                    Type = ActionType.KeyboardStep,
                    TargetX = target.X,
                    TargetY = target.Y,
                    Direction = direction,
                    WaypointIndex = FindWaypointIndex(waypoints, target)
                };
            }
            else if (totalDistance <= 3)
            {
                Console.WriteLine($"[NAV] Close target - using keyboard (distance={totalDistance})");

                // For targets within 3 squares, use keyboard navigation
                ArrowAction.ArrowDirection direction;

                // Prioritize larger movement
                if (Math.Abs(distanceX) > Math.Abs(distanceY))
                {
                    direction = distanceX > 0 ? ArrowAction.ArrowDirection.Right : ArrowAction.ArrowDirection.Left;
                }
                else
                {
                    direction = distanceY > 0 ? ArrowAction.ArrowDirection.Down : ArrowAction.ArrowDirection.Up;
                }

                Console.WriteLine($"[NAV] Using keyboard direction: {direction}");

                return new NavigationAction
                {
                    Type = ActionType.KeyboardStep,
                    TargetX = target.X,
                    TargetY = target.Y,
                    Direction = direction,
                    WaypointIndex = FindWaypointIndex(waypoints, target)
                };
            }
            else
            {
                Console.WriteLine($"[NAV] Far target - using click (distance={totalDistance})");

                // For distant targets, use click navigation
                return new NavigationAction
                {
                    Type = ActionType.ClickWaypoint,
                    TargetX = target.X,
                    TargetY = target.Y,
                    WaypointIndex = FindWaypointIndex(waypoints, target)
                };
            }
        }

        static DateTime lastPositionUpdate = DateTime.MinValue;
        static Coordinate globalLastPosition = null; // Last player position
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
        }


    // Add these variables at the top of the Program class
    static Thread soulPositionMonitorThread;
    static bool soulPositionMonitorRunning = false;
    static DateTime lastSoulChangeTime = DateTime.Now;
    static DateTime lastPositionChangeTime = DateTime.Now;
    static double lastSoulCount = 0;
    static Coordinate lastMonitoredPosition = null;
    static readonly object monitorLock = new object();

    const int MONITOR_INTERVAL_MS = 1000; // Check every second
    const int MAX_UNCHANGED_SECONDS = 45; // Stop program if no change for 5 seconds

    // Add this method to start the monitoring thread
    static void StartSoulPositionMonitorThread()
    {
        ReadMemoryValues();
        if (soulPositionMonitorThread != null && soulPositionMonitorThread.IsAlive)
        {
            Console.WriteLine("[MONITOR] Soul and position monitor thread already running");
            return;
        }

        soulPositionMonitorRunning = true;
        lastSoulCount = curSoul;
        lastMonitoredPosition = new Coordinate
        {
            X = currentX,
            Y = currentY,
            Z = currentZ
        };
        lastSoulChangeTime = DateTime.Now;
        lastPositionChangeTime = DateTime.Now;

        soulPositionMonitorThread = new Thread(SoulPositionMonitorWorker)
        {
            IsBackground = true,
            Name = "SoulPositionMonitorThread"
        };
        soulPositionMonitorThread.Start();
        Console.WriteLine("[MONITOR] Soul and position monitor thread started");
    }

    // Add this method to stop the monitoring thread
    static void StopSoulPositionMonitorThread()
    {
        soulPositionMonitorRunning = false;
        if (soulPositionMonitorThread != null && soulPositionMonitorThread.IsAlive)
        {
            soulPositionMonitorThread.Join(2000);
            Console.WriteLine("[MONITOR] Soul and position monitor thread stopped");
        }
    }

    // Add this method as the main monitoring worker
    static void SoulPositionMonitorWorker()
    {
        Console.WriteLine("[MONITOR] Soul and position monitor worker started");

        try
        {
            while (soulPositionMonitorRunning && programRunning)
            {
                try
                {
                    ReadMemoryValues();
                    DateTime now = DateTime.Now;
                    bool shouldShutdown = false;
                    string shutdownReason = "";

                    lock (monitorLock)
                    {
                        // Check if soul has changed
                        if (Math.Abs(curSoul - lastSoulCount) > 0.1) // Allow small floating point differences
                        {
                            lastSoulCount = curSoul;
                            lastSoulChangeTime = now;
                            Console.WriteLine($"[MONITOR] Soul changed to {curSoul:F1}");
                        }

                        // Check if position has changed
                        bool positionChanged = lastMonitoredPosition == null ||
                                               lastMonitoredPosition.X != currentX ||
                                               lastMonitoredPosition.Y != currentY ||
                                               lastMonitoredPosition.Z != currentZ;

                        if (positionChanged)
                        {
                            if (lastMonitoredPosition != null)
                            {
                                Console.WriteLine($"[MONITOR] Position changed from ({lastMonitoredPosition.X}, {lastMonitoredPosition.Y}, {lastMonitoredPosition.Z}) to ({currentX}, {currentY}, {currentZ})");
                            }

                            lastMonitoredPosition = new Coordinate
                            {
                                X = currentX,
                                Y = currentY,
                                Z = currentZ
                            };
                            lastPositionChangeTime = now;
                        }

                        // Check if too much time has passed without changes
                        double timeSinceLastSoulChange = (now - lastSoulChangeTime).TotalSeconds;
                        double timeSinceLastPositionChange = (now - lastPositionChangeTime).TotalSeconds;

                        // Only shut down if BOTH soul AND position haven't changed
                        if (timeSinceLastSoulChange >= MAX_UNCHANGED_SECONDS &&
                            timeSinceLastPositionChange >= MAX_UNCHANGED_SECONDS)
                        {
                            shouldShutdown = true;
                            shutdownReason = $"Both soul count and position haven't changed for {Math.Min(timeSinceLastSoulChange, timeSinceLastPositionChange):F1} seconds";
                        }

                        // Log status every 2 seconds
                        if ((DateTime.Now.Second % 2) == 0)
                        {
                            Console.WriteLine($"[MONITOR] Status - Soul: {curSoul:F1} (unchanged for {timeSinceLastSoulChange:F1} sec), " +
                                            $"Position: ({currentX}, {currentY}, {currentZ}) (unchanged for {timeSinceLastPositionChange:F1} sec)");
                        }
                    }

                    if (shouldShutdown)
                    {
                        Console.WriteLine($"\n[MONITOR] SHUTTING DOWN PROGRAM - {shutdownReason}");
                        Console.WriteLine("[MONITOR] This usually indicates the program is stuck or not functioning properly");

                        // Set the global flag to stop the program
                        programRunning = false;
                        cancellationTokenSource.Cancel();

                        // Force exit after a short delay
                        Thread.Sleep(2000);
                        Environment.Exit(0);
                    }

                    Thread.Sleep(MONITOR_INTERVAL_MS);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MONITOR] Error in monitoring loop: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MONITOR] Fatal error in monitor thread: {ex.Message}");
        }
        finally
        {
            Console.WriteLine("[MONITOR] Soul and position monitor worker stopped");
        }
    }

    // Add this method to manually reset the monitor timers (useful after teleporting/traveling)
    static void ResetSoulPositionMonitor()
    {
        lock (monitorLock)
        {
            lastSoulChangeTime = DateTime.Now;
            lastPositionChangeTime = DateTime.Now;
            ReadMemoryValues();
            lastSoulCount = curSoul;
            lastMonitoredPosition = new Coordinate
            {
                X = currentX,
                Y = currentY,
                Z = currentZ
            };
            Console.WriteLine("[MONITOR] Soul and position monitor timers reset");
        }
    }

    // Add this method to check the current monitor status
    static void PrintMonitorStatus()
    {
        lock (monitorLock)
        {
            DateTime now = DateTime.Now;
            double timeSinceLastSoulChange = (now - lastSoulChangeTime).TotalSeconds;
            double timeSinceLastPositionChange = (now - lastPositionChangeTime).TotalSeconds;

            Console.WriteLine("[MONITOR] Current Status:");
            Console.WriteLine($"  Soul: {curSoul:F1} (last changed {timeSinceLastSoulChange:F1} seconds ago)");
            Console.WriteLine($"  Position: ({currentX}, {currentY}, {currentZ}) (last changed {timeSinceLastPositionChange:F1} seconds ago)");
            Console.WriteLine($"  Monitor running: {soulPositionMonitorRunning}");
            Console.WriteLine($"  Time until shutdown: {Math.Max(0, MAX_UNCHANGED_SECONDS - timeSinceLastSoulChange):F1} sec (soul), {Math.Max(0, MAX_UNCHANGED_SECONDS - timeSinceLastPositionChange):F1} sec (position)");
        }
    }

}
