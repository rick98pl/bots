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
            //Debugger("Scanning backpack for recognized items");
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
            Debugger($"Moving to position ({TargetX}, {TargetY}, {TargetZ})");

            // Click on the waypoint
            ClickWaypoint(TargetX, TargetY);

            // Wait a moment for the click to register
            //Thread.Sleep(500);

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
                    Debugger($"Successfully reached position ({TargetX}, {TargetY}, {TargetZ})");
                    //Thread.Sleep(600);
                    return true;
                }

            }

            Debugger($"Failed to reach position ({TargetX}, {TargetY}, {TargetZ}) within {TimeoutMs}ms");
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
        public int MaxRetries { get; set; }
        public bool ExpectSpecificOutcome { get; set; }

        public RightClickAction(int delayAfterMs = 100, int maxRetries = 10, bool expectSpecificOutcome = true)
        {
            DelayAfterMs = delayAfterMs;
            MaxRetries = maxRetries;
            ExpectSpecificOutcome = expectSpecificOutcome;
        }

        public override bool Execute()
        {
            if (!ExpectSpecificOutcome)
            {
                // Simple right-click without verification
                Debugger("Right-clicking on character position");
                RightClickOnCharacter();
                return true;
            }

            // Right-click with retry logic
            Debugger($"Right-click with outcome verification (max {MaxRetries} attempts)");

            // Store original state before right-clicking
            ReadMemoryValues();
            int originalX = currentX;
            int originalY = currentY;
            int originalZ = currentZ;

            int attempt = 1;
            while (attempt <= MaxRetries)
            {
                Debugger($"Attempt {attempt}/{MaxRetries} - Right-click execution");

                // Execute the right-click
                RightClickOnCharacter();

                // Wait for the action to complete
                Thread.Sleep(Math.Max(DelayAfterMs, 200)); // Minimum 200ms for right-click processing

                // Verify if the expected outcome occurred
                if (VerifyExpectedOutcome())
                {
                    Debugger($"Success! Right-click outcome achieved on attempt {attempt}");
                    return true;
                }

                Debugger($"Right-click outcome not achieved on attempt {attempt}");

                // Optional: Reset to original state if needed
                // This is commented out as right-clicks typically don't move the character
                // if (currentX != originalX || currentY != originalY || currentZ != originalZ)
                // {
                //     var returnAction = new MoveAction(originalX, originalY, originalZ, 2000);
                //     returnAction.Execute();
                // }

                attempt++;

                // Wait before next attempt
                if (attempt <= MaxRetries)
                {
                    Thread.Sleep(300);
                }
            }

            Debugger($"Failed to achieve right-click outcome after {MaxRetries} attempts");
            return false;
        }

        private bool VerifyExpectedOutcome()
        {
            // This method should be implemented based on what specific outcome you expect
            // Examples could include:
            // - A menu appearing
            // - A dialog box opening
            // - Character state change
            // - UI element becoming visible/clickable

            // For now, returning true as placeholder
            // You should implement the specific verification logic here
            Debugger("Verifying right-click outcome...");
            return true; // Replace with actual verification logic
        }

        public override bool VerifySuccess()
        {
            // For standard verification, just wait the specified delay
            if (DelayAfterMs > 0)
            {
                Thread.Sleep(DelayAfterMs);
            }
            return true;
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

            Debugger($"Dragging {ItemCount} items from {directionName}");

            try
            {


                int groundYLocal = groundY;
                if (directionName == "backpack to ground" && Backpack == DragBackpack.MANAS)
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
                    Debugger($"Drag #{i} completed... ({i}/{ItemCount})");

                    if (DelayBetweenDrags > 0 && i < ItemCount)
                    {
                        Thread.Sleep(DelayBetweenDrags);
                    }
                }

                Debugger($"All {ItemCount} item drags completed ({directionName}).");
                return true;
            }
            catch (Exception ex)
            {
                Debugger($"Error during drag action: {ex.Message}");
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
            Debugger($"Pressing {keyName}");

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
        public bool ExpectZChange { get; set; } = true; // New property to indicate if this arrow action should change Z

        public ArrowAction(ArrowDirection direction, int delayMs = 100, bool expectZChange = true)
        {
            Direction = direction;
            DelayMs = delayMs;
            ExpectZChange = expectZChange;
            MaxRetries = 10; // Set higher retry count for Z-level changes
        }

        public override bool Execute()
        {
            if (!ExpectZChange)
            {
                // Normal arrow key press without Z-level verification
                int keyCode = GetArrowKeyCode(Direction);
                string directionName = Direction.ToString();
                Debugger($"Pressing Arrow {directionName}");
                SendKeyPress(keyCode);
                return true;
            }
            // Z-level change verification logic
            Debugger($"Arrow {Direction} with Z-level change verification");
            // Store original position
            ReadMemoryValues();
            int originalX = currentX;
            int originalY = currentY;
            int originalZ = currentZ;
            Debugger($"Original position: ({originalX}, {originalY}, {originalZ})");
            int maxAttempts = MaxRetries;
            int attempt = 1;
            while (attempt <= maxAttempts)
            {
                Debugger($"Attempt {attempt}/{maxAttempts} - Z-level change");
                // Execute the arrow key press
                int keyCode = GetArrowKeyCode(Direction);
                SendKeyPress(keyCode);
                // Wait for the action to complete
                Thread.Sleep(Math.Max(DelayMs, 500)); // Minimum 500ms for Z-level changes
                                                      // Check if Z-level changed
                ReadMemoryValues();
                bool zChanged = currentZ != originalZ;

                // Check if both X and Y changed by more than 100
                int xChange = Math.Abs(currentX - originalX);
                int yChange = Math.Abs(currentY - originalY);
                bool significantMovement = xChange > 100 && yChange > 100;

                if (significantMovement)
                {
                    Debugger($"Both X and Y changed by more than 100 - bypassing Z-level check");
                    Debugger($"X change: {xChange}, Y change: {yChange}");
                    return true;
                }

                Debugger($"After arrow press: ({currentX}, {currentY}, {currentZ}) - Z changed: {zChanged}");
                if (zChanged)
                {
                    Debugger($"Success! Z-level changed from {originalZ} to {currentZ}");
                    return true;
                }
                // Z didn't change - return to original position if we moved
                if (currentX != originalX || currentY != originalY)
                {
                    Debugger($"Z didn't change but position moved. Returning to original position...");
                    // Create and execute a MoveAction to return to original position
                    var returnAction = new MoveAction(originalX, originalY, originalZ, 2000);
                    // Execute the return move
                    if (!returnAction.Execute())
                    {
                        Debugger($"Failed to execute return movement on attempt {attempt}");
                    }
                    // Verify the return move
                    if (!returnAction.VerifySuccess())
                    {
                        Debugger($"Failed to verify return movement on attempt {attempt}");
                        // Continue to next attempt even if return failed
                    }
                    else
                    {
                        Debugger($"Successfully returned to original position");
                    }
                }
                attempt++;
                // Wait before next attempt
                if (attempt <= maxAttempts)
                {
                    Thread.Sleep(500);
                }
            }
            Debugger($"Failed to change Z-level after {maxAttempts} attempts");
            return false;
        }

        public override bool VerifySuccess()
        {
            if (!ExpectZChange)
            {
                // For normal arrow actions, just wait the delay
                if (DelayMs > 0)
                {
                    Thread.Sleep(DelayMs);
                }
                return true;
            }

            // For Z-level changes, the verification is already done in Execute()
            // so we just return true here
            return true;
        }

        public override string GetDescription()
        {
            string baseDescription = $"Press Arrow {Direction}";
            if (ExpectZChange)
            {
                baseDescription += " (with Z-level verification)";
            }
            return baseDescription;
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


        actionSequence.Add(new MoveAction(32817, 32809, 7));

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

        Debugger($"Initialized action sequence with {actionSequence.Count} actions:");
        for (int i = 0; i < actionSequence.Count; i++)
        {
            Debugger($"  {i + 1}: {actionSequence[i].GetDescription()}");
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
        //actionSequence.Add(new MoveAction(baseX + 23, baseY + 24, baseZ + 0));
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

        Debugger($"Initialized action sequence with {actionSequence.Count} actions:");
        for (int i = 0; i < actionSequence.Count; i++)
        {
            Debugger($"  {i + 1}: {actionSequence[i].GetDescription()}");
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
        //actionSequence.Add(new MoveAction(baseX + 23, baseY + 24, baseZ + 0));
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

        Debugger($"Initialized action sequence with {actionSequence.Count} actions:");
        for (int i = 0; i < actionSequence.Count; i++)
        {
            Debugger($"  {i + 1}: {actionSequence[i].GetDescription()}");
        }


    }

    // Updated ExecuteActionSequence with retry logic
    static void ExecuteActionSequence()
    {
        try
        {
            actionSequenceRunning = true;
            currentActionIndex = 0;

            Debugger("Action sequence started...");

            for (currentActionIndex = 0; currentActionIndex < actionSequence.Count; currentActionIndex++)
            {
                if (cancellationTokenSource.Token.IsCancellationRequested || !programRunning)
                {
                    Debugger("Action sequence cancelled.");
                    break;
                }

                var action = actionSequence[currentActionIndex];
                Debugger($"Executing action {currentActionIndex + 1}/{actionSequence.Count}: {action.GetDescription()}");

                bool success = false;
                int retryCount = 0;

                // Retry logic
                while (!success && retryCount < action.MaxRetries)
                {
                    if (retryCount > 0)
                    {
                        Debugger($"Retry attempt {retryCount}/{action.MaxRetries} for action: {action.GetDescription()}");
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
                            Debugger($"HP low ({hpPercent:F1}%) - pressed F1");
                        }
                    }

                    // Mana check
                    if (mana <= MANA_THRESHOLD)
                    {
                        if ((DateTime.Now - lastManaAction).TotalMilliseconds >= 2000)
                        {
                            SendKeyPress(VK_F3);
                            lastManaAction = DateTime.Now;
                            Debugger($"Mana low ({mana:F1}) - pressed F3");
                        }
                    }

                    bool executeSuccess = action.Execute();

                    if (executeSuccess)
                    {
                        // Verify if the action was successful
                        success = action.VerifySuccess();

                        if (!success)
                        {
                            Debugger($"Action executed but verification failed.");
                        }
                    }
                    else
                    {
                        Debugger($"Action execution failed.");
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
                    Debugger($"Action {currentActionIndex + 1} failed after {action.MaxRetries} attempts. Exiting program.");
                    programRunning = false;
                    break;
                }

                // Read and display current coordinates after each successful action
                ReadMemoryValues();
                //Debugger($"Current coordinates: ({currentX}, {currentY}, {currentZ})");

                // Small delay between actions
                Thread.Sleep(200);
            }

            if (currentActionIndex >= actionSequence.Count && programRunning)
            {
                Debugger("\nAction sequence completed successfully!");
            }
        }
        catch (Exception ex)
        {
            Debugger($"Error during action sequence: {ex.Message}");
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
            Debugger("[MOTION] Motion detection thread already running");
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
        Debugger("[MOTION] Motion detection thread started");
    }

    // Add this method to stop the motion detection thread
    static void StopMotionDetectionThread()
    {
        motionDetectionRunning = false;
        if (motionDetectionThread != null && motionDetectionThread.IsAlive)
        {
            motionDetectionThread.Join(2000);
            Debugger("[MOTION] Motion detection thread stopped");
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
                Debugger($"[MOTION] Delaying {spellName} cast - too close to previous spell");
                return false;
            }

            Debugger($"[MOTION] Casting {spellName}");
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
        Debugger($"[MOTION] Utani Gran Hur validation - Speed: {speed:F1}, Success: {success}");
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
        Debugger("[MOTION] Motion detection worker started");

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
                            Debugger($"[MOTION] Initial position set: ({currentPosition.X}, {currentPosition.Y}, {currentPosition.Z})");
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
                                    //Debugger("[MOTION] First movement detected since start - utana vid now eligible");
                                }

                                int distanceX = Math.Abs(currentPosition.X - lastKnownPosition.X);
                                int distanceY = Math.Abs(currentPosition.Y - lastKnownPosition.Y);
                                if (distanceX > 1 || distanceY > 1 || currentPosition.Z != lastKnownPosition.Z)
                                {
                                    //Debugger($"[MOTION] Significant movement detected: ({lastKnownPosition.X}, {lastKnownPosition.Y}, {lastKnownPosition.Z}) -> ({currentPosition.X}, {currentPosition.Y}, {currentPosition.Z})");
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
                                    Debugger("[MOTION] Character stopped moving");
                                }
                            }
                        }
                    }

                    DateTime now = DateTime.Now;
                    bool needsUtanaVid = invisibilityCode == 1;

                    ReadMemoryValues();
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
                            Debugger("[MOTION] Retrying utana vid (previous attempt failed)");
                        }

                        if (shouldCastUtanaVid && curMana > 440 && (currentX >= 32706))
                        {
                            lastUtanaVidAttemptTime = now;

                            if (TryCastSpell(VK_F11, "utana vid", ref lastUtanaVidTime, 0))
                            {
                                lastUtanaVidTime = now; // Update the successful cast time

                                if (CheckUtanaVidSuccess())
                                {
                                    Debugger("[MOTION] Utana vid successful - invisibility removed");
                                }
                                else
                                {
                                    Debugger("[MOTION] Utana vid failed - still invisible, will retry");
                                    lastUtanaVidTime = lastUtanaVidAttemptTime - TimeSpan.FromSeconds(UTANA_VID_INTERVAL_SECONDS);
                                }
                            }
                        }
                    }
                    // Check if we need to cast utani gran hur (backslash)
                    if (characterIsMoving)
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
                            Debugger("[MOTION] Retrying utani gran hur (previous attempt failed)");
                        }

                        if (currentZ != 8 && shouldCastUtaniGranHur && curMana > 100)
                        {
                            lastUtaniGranHurAttemptTime = now;

                            if (TryCastSpell(BACKSLASH, "utani gran hur", ref lastUtaniGranHurTime, 0))
                            {
                                lastUtaniGranHurTime = now; // Update the successful cast time

                                if (CheckUtaniGranHurSuccess())
                                {
                                    Debugger("[MOTION] Utani gran hur successful - speed increased");
                                }
                                else
                                {
                                    Debugger("[MOTION] Utani gran hur failed - speed too low, will retry");
                                    lastUtaniGranHurTime = lastUtaniGranHurAttemptTime - TimeSpan.FromSeconds(UTANI_GRAN_HUR_INTERVAL_SECONDS);
                                }
                            }
                        }
                    }

                    Thread.Sleep(POSITION_CHECK_INTERVAL_MS);
                }
                catch (Exception ex)
                {
                    Debugger($"[MOTION] Error in motion detection loop: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }
        }
        catch (Exception ex)
        {
            Debugger($"[MOTION] Fatal error in motion detection thread: {ex.Message}");
        }
        finally
        {
            Debugger("[MOTION] Motion detection worker stopped");
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
            Debugger("[MOTION] Motion detection reset - movement eligibility reset");
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
            Debugger("[MOTION] Spell timers reset");
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

            Debugger("[MOTION] Spell Status:");

            // Utani Gran Hur status
            if (lastUtaniGranHurAttemptTime > lastUtaniGranHurTime && speed < MIN_SPEED_FOR_UTANI_GRAN_HUR)
            {
                Debugger($"  Utani Gran Hur: {(utaniGranHurRetryCooldown > 0 ? $"{utaniGranHurRetryCooldown:F1}s retry cooldown" : "Ready to retry")} (last attempt failed)");
            }
            else
            {
                Debugger($"  Utani Gran Hur: {(utaniGranHurCooldown > 0 ? $"{utaniGranHurCooldown:F1}s cooldown" : "Ready")}");
            }

            // Utana Vid status
            if (movementDetectedSinceStart)
            {
                if (lastUtanaVidAttemptTime > lastUtanaVidTime && invisibilityCode == 1)
                {
                    Debugger($"  Utana Vid: {(utanaVidRetryCooldown > 0 ? $"{utanaVidRetryCooldown:F1}s retry cooldown" : "Ready to retry")} (last attempt failed)");
                }
                else
                {
                    Debugger($"  Utana Vid: {(utanaVidCooldown > 0 ? $"{utanaVidCooldown:F1}s cooldown" : "Ready")}");
                }
            }
            else
            {
                Debugger("  Utana Vid: Waiting for movement before becoming eligible");
            }

            Debugger($"  Character moving: {IsCharacterMoving()}");
            Debugger($"  Movement detected since start: {movementDetectedSinceStart}");
            Debugger($"  Current speed: {speed:F1}");
            Debugger($"  Invisibility code: {invisibilityCode}");
        }
    }

    // Modify your Main method to start the motion detection thread
    // Add this line after you find the window handle and before the main loop:
    // StartMotionDetectionThread();

    // Also modify the quit section in your Main method:
    // Before Environment.Exit(0), add:
    // StopMotionDetectionThread();
    static Thread qKeyListenerThread;
    static bool qKeyListenerRunning = false;

    // Add this method to start the Q key listener thread
    static void StartQKeyListenerThread()
    {
        if (qKeyListenerThread != null && qKeyListenerThread.IsAlive)
        {
            Debugger("[Q-KEY] Q key listener thread already running");
            return;
        }

        qKeyListenerRunning = true;
        qKeyListenerThread = new Thread(QKeyListenerWorker)
        {
            IsBackground = true,
            Name = "QKeyListenerThread"
        };
        qKeyListenerThread.Start();
        Debugger("[Q-KEY] Q key listener thread started - press Q at any time to quit");
    }

    static void QKeyListenerWorker()
    {
        Debugger("[Q-KEY] Q key listener worker started");

        try
        {
            while (qKeyListenerRunning && programRunning)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true).Key;

                        if (key == ConsoleKey.Q)
                        {
                            Debugger("\n[Q-KEY] Q key pressed - shutting down program...");

                            // Set flags to stop everything
                            programRunning = false;
                            qKeyListenerRunning = false;
                            actionSequenceRunning = false;
                            cancellationTokenSource.Cancel();

                            // Stop all other threads
                            StopMotionDetectionThread();
                            StopSoulPositionMonitorThread();

                            Debugger("[Q-KEY] Cleanup completed - exiting...");
                            Environment.Exit(0);
                        }
                    }

                    // Check every 100ms for better responsiveness
                    Thread.Sleep(100);
                }
                catch (Exception ex)
                {
                    Debugger($"[Q-KEY] Error in Q key listener: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }
        }
        catch (Exception ex)
        {
            Debugger($"[Q-KEY] Fatal error in Q key listener thread: {ex.Message}");
        }
        finally
        {
            Debugger("[Q-KEY] Q key listener worker stopped");
        }
    }

    static int f2ClickCount = 0;
    const int MAX_F2_CLICKS = 20;
    static DateTime lastF2AttemptTime = DateTime.MinValue;
    static double lastManaBeforeF2 = 0;
    static bool hasExecutedSequencesAfterF2Limit = false; // Flag to track if we've executed sequences after F2 limit


    static void Main()
    {
        Debugger("Starting RealeraDX Auto-Potions...");
        ShowAllProcessesWithWindows();

        // Initialize action sequence
        InitializeActionSequence();

        // Find the process
        Process process = FindRealeraProcess();
        if (process == null)
        {
            Debugger("RealeraDX process not found!");
            return;
        }

        // Get process handle
        processHandle = OpenProcess(PROCESS_ALL_ACCESS, false, process.Id);
        moduleBase = process.MainModule.BaseAddress;

        // Find window handle
        FindRealeraWindow(process);

        ReadMemoryValues();
        Debugger($"Found RealeraDX process (ID: {process.Id})");
        Debugger($"Window handle: {targetWindow}");
        Debugger("\nThresholds:");
        Debugger($"HP: {HP_THRESHOLD}%");
        Debugger($"Mana: {MANA_THRESHOLD}");
        Debugger($"Soul: {SOUL_THRESHOLD} (absolute value)");
        Debugger($"X: {currentX} (absolute value)");
        Debugger($"Y: {currentY} (absolute value)");
        Debugger($"Z: {currentZ} (absolute value)");
        Debugger($"InvisibilityCode: {invisibilityCode}");
        Debugger($"Speed: {speed}");
        Debugger("\nControls:");
        Debugger("Q - Quit");
        Debugger("E - Drag items from backpack to ground (8x)");
        Debugger("R - Drag items from ground to backpack (8x)");
        Debugger("P - Execute action sequence");
        Debugger("M - Reset motion detection");
        Debugger("N - Check if character is moving");
        Debugger("T - Reset spell timers");
        Debugger("S - Show spell status");
        Debugger("\nAuto-spells:");
        Debugger("- Utani Gran Hur (\\): Every 20 seconds when moving");
        Debugger("- Utana Vid (F11): Every 3 minutes when invisible");
        Debugger("- Minimum 3 seconds between any spells");

        StartQKeyListenerThread();
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
                        Debugger("\nQuitting...");
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
                            Debugger("Starting to drag items from backpack to ground (8x)...");
                            currentDragCount = 0;
                            Task.Run(() => StartItemDragging(false));
                        }
                        else
                        {
                            Debugger("Item dragging already in progress...");
                        }
                    }
                    else if (key == ConsoleKey.R)
                    {
                        if (!itemDragInProgress)
                        {
                            Debugger("Starting to drag items from ground to backpack (8x)...");
                            currentDragCount = 0;
                            Task.Run(() => StartItemDragging(true));
                        }
                        else
                        {
                            Debugger("Item dragging already in progress...");
                        }
                    }
                    else if (key == ConsoleKey.P)
                    {
                        if (!actionSequenceRunning)
                        {
                            Debugger("Starting action sequence...");
                            Task.Run(() => ExecuteActionSequence());
                        }
                        else
                        {
                            Debugger("Action sequence already in progress...");
                        }
                    }
                    else if (key == ConsoleKey.S)
                    {
                        PrintSpellStatus();
                    }
                    else if (key == ConsoleKey.M)
                    {
                        Debugger("Resetting motion detection...");
                        ResetMotionDetection();
                    }
                    else if (key == ConsoleKey.N)
                    {
                        bool moving = IsCharacterMoving();
                        Debugger($"Character is currently moving: {moving}");
                    }
                    else if (key == ConsoleKey.T)
                    {
                        Debugger("Resetting spell timers...");
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
                        Debugger($"HP low ({hpPercent:F1}%) - pressed F1");
                    }
                }

                // Mana check
                if (mana <= MANA_THRESHOLD)
                {
                    if ((DateTime.Now - lastManaAction).TotalMilliseconds >= 2000)
                    {
                        SendKeyPress(VK_F3);
                        lastManaAction = DateTime.Now;
                        Debugger($"Mana low ({mana:F1}) - pressed F3");
                    }
                }
                // Mana & Soul check
                else if (curMana >= MANA_THRESHOLD)
                {
                    if ((DateTime.Now - lastManaAction).TotalMilliseconds >= 2000)
                    {
                        PressF2WithValidation();
                        Debugger($"Mana>900 ({curMana:F0}), Soul: ({curSoul:F1}) - attempted F2");
                    }
                }

                if (currentZ == 8)
                {
                    tarantulaSeqeunce();
                    ExecuteActionSequence();
                }

                if (f2ClickCount >= MAX_F2_CLICKS) {
                    if (curSoul >= 100)
                    {
                        InitializeMiddleSequence();
                        ExecuteActionSequence();
                        SendKeyPress(VK_F2);
                        lastManaAction = DateTime.Now;
                        Debugger($"Mana>900 ({curMana:F0}), Soul>{SOUL_THRESHOLD} ({curSoul:F1}) - pressed F2");
                        SendKeyPress(VK_F2);
                        lastManaAction = DateTime.Now;
                        Debugger($"Mana>900 ({curMana:F0}), Soul>{SOUL_THRESHOLD} ({curSoul:F1}) - pressed F2");
                        f2ClickCount = 0;
                    }
                    else
                    {
                        InitializeActionSequence();
                        ExecuteActionSequence();
                        f2ClickCount = 0;
                    }
                }
                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                Debugger($"Error: {ex.Message}");
                Thread.Sleep(1000);
            }
        }
    }

    static void PressF2WithValidation()
    {
        if (f2ClickCount >= MAX_F2_CLICKS)
        {
            Debugger($"[F2] F2 limit reached ({f2ClickCount}/{MAX_F2_CLICKS}). Skipping F2 press.");
            return;
        }

        // Record mana before F2 press
        ReadMemoryValues();
        double manaBeforeF2 = curMana;
        DateTime attemptTime = DateTime.Now;

        // Press F2
        SendKeyPress(VK_F2);
        lastManaAction = DateTime.Now;
        Debugger($"[F2] Pressed F2. Mana before: {manaBeforeF2:F0}");

        // Wait for the effect to apply
        Thread.Sleep(1000); // Give it a bit more time to register

        // Read mana after F2
        ReadMemoryValues();
        double manaAfterF2 = curMana;
        double manaDrop = manaBeforeF2 - manaAfterF2;

        // Validate the click
        if (manaDrop >= 800)
        {
            f2ClickCount++;
            Debugger($"[F2] F2 validated successfully! Mana dropped by {manaDrop:F0}. Total F2 clicks: {f2ClickCount}/{MAX_F2_CLICKS}");
        }
        else
        {
            Debugger($"[F2] F2 failed validation! Mana only dropped by {manaDrop:F0} (expected at least 800). Not counting this click.");
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

        Debugger($"Right-clicked on character at screen coordinates ({groundX}, {groundY})");
    }

    static void StartItemDragging(bool reverseDirection)
    {
        try
        {
            itemDragInProgress = true;
            string direction = reverseDirection ? "ground to backpack" : "backpack to ground";
            Debugger($"Item dragging task started ({direction})...");

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
                    Debugger($"Item dragging interrupted during drag #{currentDragCount}");
                    cancellationTokenSource = new CancellationTokenSource();
                    break;
                }

                DragItem(sourcePoint.X, sourcePoint.Y, destPoint.X, destPoint.Y);
                Debugger($"Drag #{currentDragCount} completed... ({currentDragCount}/{MAX_DRAGS})");

                try
                {
                    cancellationTokenSource.Token.WaitHandle.WaitOne(100);
                }
                catch (OperationCanceledException)
                {
                    Debugger("Item dragging cancelled");
                    cancellationTokenSource = new CancellationTokenSource();
                    break;
                }
            }

            if (currentDragCount > MAX_DRAGS)
            {
                Debugger($"All {MAX_DRAGS} item drags completed ({direction}).");
            }
        }
        catch (Exception ex)
        {
            Debugger($"Error during item dragging: {ex.Message}");
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
        Debugger($"[DEBUG] Moving cursor to source: Game({fromX}, {fromY}) -> Screen({sourceScreenPoint.X}, {sourceScreenPoint.Y})");
        //SetCursorPos(fromX, fromY);
        //Thread.Sleep(4000);

        // DEBUG: Show destination position
        Debugger($"[DEBUG] Moving cursor to destination: Game({toX}, {toY}) -> Screen({destScreenPoint.X}, {destScreenPoint.Y})");
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

        Debugger($"Dragged item from ({fromX}, {fromY}) to ({toX}, {toY})");
    }

    static Process FindRealeraProcess()
    {
        var processes = Process
            .GetProcesses()
            .Where(p => p.ProcessName == processName)
            .ToArray();

        if (processes.Length == 0)
        {
            Debugger($"Process '{processName}' not found.");
            return null;
        }

        var targetProcess = processes.FirstOrDefault(p =>
            !string.IsNullOrEmpty(p.MainWindowTitle) &&
            p.MainWindowTitle.Contains("Knajtka Martynka", StringComparison.OrdinalIgnoreCase));

        if (targetProcess != null)
        {
            Debugger($"Found target process: {targetProcess.ProcessName} (ID: {targetProcess.Id})");
            Debugger($"Window Title: {targetProcess.MainWindowTitle}");
            return targetProcess;
        }
        else if (processes.Length == 1)
        {
            var process = processes[0];
            Debugger($"One process found: {process.ProcessName} (ID: {process.Id})");
            Debugger($"Window Title: {process.MainWindowTitle}");
            Debugger("WARNING: Process doesn't contain 'Knajtka Martynka' in title!");
            return process;
        }
        else
        {
            Debugger($"Multiple processes found with name '{processName}':");
            for (int i = 0; i < processes.Length; i++)
            {
                Debugger($"{i + 1}: ID={processes[i].Id}, Name={processes[i].ProcessName}, Window Title={processes[i].MainWindowTitle}, StartTime={(processes[i].StartTime)}");
            }
            Debugger("Enter the number of the process you want to select (1-9):");
            string input = Console.ReadLine();
            if (
                int.TryParse(input, out int choice)
                && choice >= 1
                && choice <= processes.Length
            )
            {
                var selectedProc = processes[choice - 1];
                Debugger($"Selected process: {selectedProc.ProcessName} (ID: {selectedProc.Id})");
                Debugger($"Window Title: {selectedProc.MainWindowTitle}");
                return selectedProc;
            }
            else
            {
                Debugger("Invalid selection. Please try again.");
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
        Debugger("\n=== REALERA PROCESSES WITH WINDOWS ===");
        var processes = Process.GetProcesses()
            .Where(p => p.ProcessName.Contains("Realera", StringComparison.OrdinalIgnoreCase))
            .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
            .OrderBy(p => p.ProcessName);

        foreach (var process in processes)
        {
            try
            {
                Debugger($"Process: {process.ProcessName}");
                Debugger($"  ID: {process.Id}");
                Debugger($"  Main Window Title: '{process.MainWindowTitle}'");
                Debugger($"  Window Handle: {process.MainWindowHandle}");
            }
            catch
            {
                // Some processes might not be accessible
            }
        }
        Debugger("=======================================\n");
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

                    Debugger($"[DROPS] Converting scan coords ({backpackPosition.Value.X}, {backpackPosition.Value.Y}) to game coords ({itemX}, {itemY})");
                    Debugger($"[DROPS] Converting game coords to screen coords: ({itemX}, {itemY}) -> ({sourcePoint.X}, {sourcePoint.Y})");
                    Debugger($"[DROPS] Destination screen coords: ({leftBackpackX}, {leftBackpackY}) -> ({destPoint.X}, {destPoint.Y})");

                    // Pass screen coordinates to DragItem
                    DragItem(sourcePoint.X, sourcePoint.Y, destPoint.X, destPoint.Y);
                }
            }
        }
        catch (Exception ex)
        {
            Debugger($"[DROPS] Error scanning backpack: {ex.Message}");
        }
    }

    static void SaveDebugScreenshot(Mat backpackArea, int scanLeft, int scanTop, int scanWidth, int scanHeight)
    {
        return;
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

            Debugger($"[DEBUG] Screenshot saved: {filename}");
            Debugger($"[DEBUG] Scan area: Left={scanLeft}, Top={scanTop}, Width={scanWidth}, Height={scanHeight}");

            debugScreenshotCounter++;
        }
        catch (Exception ex)
        {
            Debugger($"[DEBUG] Error saving screenshot: {ex.Message}");
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
            Debugger($"[CAPTURE] Screenshot error: {ex.Message}");
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
                Debugger("[DROPS] purplebackpack.png not found in recognizedItems folder");
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

                        Debugger($"[DROPS] Found purple backpack at scan area position ({backpackCenter.X}, {backpackCenter.Y}) with confidence {maxVal:F2}");
                        return backpackCenter;
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Debugger($"[DROPS] Error finding purple backpack: {ex.Message}");
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

            Debugger($"[DEBUG] Found item screenshot saved: {filename}");
        }
        catch (Exception ex)
        {
            Debugger($"[DEBUG] Error saving found item screenshot: {ex.Message}");
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
                Debugger($"[RETURN] Current position: ({currentX}, {currentY}, {currentZ})");

                // Make sure coords are loaded
                if (loadedCoords == null)
                {
                    string json = File.ReadAllText(cordsFilePath);
                    loadedCoords = JsonSerializer.Deserialize<CoordinateData>(json);

                    if (loadedCoords == null || loadedCoords.cords.Count == 0)
                    {
                        Debugger("[RETURN] ERROR: No waypoints loaded");
                        return;
                    }
                }

                List<Coordinate> waypoints = loadedCoords.cords;

                // 3. Get coordinates of [0] waypoint (first waypoint)
                if (waypoints == null || waypoints.Count == 0)
                {
                    Debugger("[RETURN] ERROR: No waypoints available");
                    return;
                }

                Coordinate firstWaypoint = waypoints[0];
                Debugger($"[RETURN] Target waypoint [0]: ({firstWaypoint.X}, {firstWaypoint.Y}, {firstWaypoint.Z})");

                // Check if we're already at the first waypoint
                if (IsAtPosition(firstWaypoint.X, firstWaypoint.Y, firstWaypoint.Z))
                {
                    Debugger("[RETURN] Already at first waypoint [0]");
                    currentCoordIndex = 0;
                    globalLastIndex = 0;
                    return;
                }

                // 2. Find closest waypoint to current position
                currentCoordIndex = FindClosestWaypointIndex(waypoints, currentX, currentY, currentZ);
                Debugger($"[RETURN] Closest waypoint to current position: [{currentCoordIndex}]");

                // Analyze both paths and choose the optimal one
                PathAnalysisResult pathAnalysis = AnalyzePaths(waypoints, currentCoordIndex);
                List<int> optimalPath = pathAnalysis.UseReversePath ? pathAnalysis.ReversePath : pathAnalysis.ForwardPath;
                string direction = pathAnalysis.UseReversePath ? "REVERSE" : "FORWARD";

                Debugger($"[RETURN] Using {direction} path ({optimalPath.Count} waypoints)");
                Debugger($"[RETURN] Forward path: {pathAnalysis.ForwardPath.Count} waypoints, Reverse path: {pathAnalysis.ReversePath.Count} waypoints");

                // 4. Execute path using furthest reachable waypoint logic
                DateTime returnStartTime = DateTime.Now;
                const int RETURN_TIMEOUT_MINUTES = 3;
                int pathProgress = 0; // Index in our optimal path

                while (!IsAtPosition(firstWaypoint.X, firstWaypoint.Y, firstWaypoint.Z))
                {
                    // Safety timeout check
                    if ((DateTime.Now - returnStartTime).TotalMinutes > RETURN_TIMEOUT_MINUTES)
                    {
                        Debugger($"[RETURN] TIMEOUT: Failed to reach first waypoint within {RETURN_TIMEOUT_MINUTES} minutes");
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
                            Debugger($"HP low ({hpPercent:F1}%) - pressed F1");
                        }
                    }

                    // Mana check
                    if (mana <= MANA_THRESHOLD)
                    {
                        if ((DateTime.Now - lastManaAction).TotalMilliseconds >= 2000)
                        {
                            SendKeyPress(VK_F3);
                            lastManaAction = DateTime.Now;
                            Debugger($"Mana low ({mana:F1}) - pressed F3");
                        }
                    }

                    // Find furthest reachable waypoint along our optimal path
                    Coordinate nextTarget = FindFurthestReachableInPath(waypoints, optimalPath, pathProgress, firstWaypoint);

                    if (nextTarget == null)
                    {
                        Debugger("[RETURN] ERROR: Could not find next target in path");
                        break;
                    }

                    // Skip if we're already at the target
                    if (IsAtPosition(nextTarget.X, nextTarget.Y, nextTarget.Z))
                    {
                        Debugger("[RETURN] Already at target position");
                        pathProgress++;
                        continue;
                    }

                    Debugger($"[RETURN] Moving to: ({nextTarget.X}, {nextTarget.Y}, {nextTarget.Z})");

                    // Check if we need to handle Z-level change
                    if (nextTarget.Z != currentZ)
                    {
                        if (nextTarget.Z > currentZ)
                        {
                            Debugger("[RETURN] Need to go UP");
                            RightClickOnCharacter();
                        }
                        else
                        {
                            Debugger("[RETURN] Need to go DOWN");
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

                Debugger("[RETURN] Successfully reached first waypoint [0]");
                currentCoordIndex = 0;
                globalLastIndex = 0;
            }
            catch (Exception ex)
            {
                Debugger($"[RETURN] ERROR: {ex.Message}");
                Debugger($"[RETURN] Stack trace: {ex.StackTrace}");
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

            Debugger($"[PATH] Forward path: {forwardSet.Count} unique waypoints");
            Debugger($"[PATH] Reverse path: {reverseSet.Count} unique waypoints");

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
                Debugger($"[RETURN] Can reach final target directly (distance: {distanceToFinal})");
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
                Debugger("[RETURN] No waypoint in path reachable, using directional movement");
                return CalculateDirectionalMove(currentX, currentY, currentZ, finalTarget);
            }

            Debugger($"[RETURN] Found furthest reachable waypoint at distance {furthestDistance}");
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
                    Debugger($"[RETURN] Reached waypoint [{waypointIndex}] in path (progress: {i})");
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
                    Debugger("[FIGHT] No waypoints loaded");
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
                        Debugger("\n[STOP] 'X' key pressed. Stopping iteration...");
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
                        Debugger($"HP low ({hpPercent:F1}%) - pressed F1");
                    }
                }

                // Mana check
                if (mana <= MANA_THRESHOLD)
                {
                    if ((DateTime.Now - lastManaAction).TotalMilliseconds >= 2000)
                    {
                        SendKeyPress(VK_F3);
                        lastManaAction = DateTime.Now;
                        Debugger($"Mana low ({mana:F1}) - pressed F3");
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
                    Debugger($"[DEBUG] Initialized global state. Starting at waypoint index: {currentCoordIndex}");
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
                                Debugger("\n[STOP] 'X' key pressed during combat. Stopping iteration...");
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
                                Debugger($"HP low ({hpPercent:F1}%) - pressed F1");
                            }
                        }

                        // Mana check
                        if (mana <= MANA_THRESHOLD)
                        {
                            if ((DateTime.Now - lastManaAction).TotalMilliseconds >= 2000)
                            {
                                SendKeyPress(VK_F3);
                                lastManaAction = DateTime.Now;
                                Debugger($"Mana low ({mana:F1}) - pressed F3");
                            }
                        }

                        if (targetId != 0)
                        {
                            Debugger($"[COMBAT] Fighting target {targetId}...");
                            Thread.Sleep(1000);
                            ReadMemoryValues();

                            ReadMemoryValues();
                            if (targetId == 0)
                            {
                                Debugger("[COMBAT] Target killed!");
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
                        Debugger($"[PLAY] Executing keyboard step: {action.Direction}");

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
                            Debugger("[PLAY] Reached keyboard target!");
                            currentCoordIndex = action.WaypointIndex;
                            globalLastIndex = currentCoordIndex;
                        }

                        // Recovery mode tracking
                        if (isInRecoveryMode)
                        {
                            recoveryAttempts++;
                            if (recoveryAttempts > 20)
                            {
                                Debugger("[PLAY] Too many recovery attempts - forcing exit of recovery mode");
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
                            Debugger($"[MOVE] No movement detected. Count: {noMovementCount}/{MAX_NO_MOVEMENT_COUNT}");

                            if (noMovementCount >= MAX_NO_MOVEMENT_COUNT)
                            {
                                Debugger($"[MOVE] Character hasn't moved for {noMovementCount} checks. Returning early.");
                                return;
                            }
                        }
                        else
                        {
                            noMovementCount = 0; // Reset counter if character moved
                            Debugger($"[MOVE] Character moved from ({lastPosX},{lastPosY}) to ({currentX},{currentY})");
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
                        Debugger($"[MOVE] Reached target position ({targetX},{targetY})");
                        Thread.Sleep(400);
                        return;
                    }
                }
            }


            Debugger($"[MOVE] Timeout reached after {TIMEOUT_MS}ms");
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

            Debugger($"[RECOVERY] Creating step from ({currentX},{currentY}) toward ({target.X},{target.Y})");
            Debugger($"[RECOVERY] Delta: X={deltaX}, Y={deltaY}");

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
                Debugger("[RECOVERY] Already at target!");
                return new Coordinate { X = currentX, Y = currentY, Z = currentZ };
            }

            Coordinate recoveryStep = new Coordinate
            {
                X = currentX + stepX,
                Y = currentY + stepY,
                Z = currentZ
            };

            Debugger($"[RECOVERY] Created step: ({recoveryStep.X},{recoveryStep.Y},{recoveryStep.Z})");
            Debugger($"[RECOVERY] Step size: X={stepX}, Y={stepY}");

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
            Debugger($"[NAV] Looking for furthest reachable waypoint from ({currentX},{currentY},{currentZ})");
            Debugger($"[NAV] Current waypoint index: {currentCoordIndex}");
            Debugger($"[NAV] Global last index: {globalLastIndex}");

            // Sync currentCoordIndex with globalLastIndex if they're out of sync
            if (globalLastIndex >= 0 && Math.Abs(globalLastIndex - currentCoordIndex) <= maxBacktrackDistance)
            {
                if (globalLastIndex != currentCoordIndex)
                {
                    Debugger($"[NAV] Syncing currentCoordIndex ({currentCoordIndex}) with globalLastIndex ({globalLastIndex})");
                    currentCoordIndex = globalLastIndex;
                }
            }

            // Check if we've reached our last targeted waypoint
            if (lastTargetedIndex >= 0 && lastTargetedWaypoint != null)
            {
                int distanceToTarget = Math.Abs(lastTargetedWaypoint.X - currentX) + Math.Abs(lastTargetedWaypoint.Y - currentY);
                if (distanceToTarget <= 2) // Close enough to consider "reached"
                {
                    Debugger($"[NAV] Reached targeted waypoint {lastTargetedIndex}. Advancing current index from {currentCoordIndex} to {lastTargetedIndex}");
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
                Debugger($"[NAV] Checking waypoint {checkIndex}: ({check.X},{check.Y},{check.Z})");

                // Handle Z-level changes for the very next waypoint (i == 1)
                if (check.Z != currentZ)
                {
                    if (i == 1)
                    {
                        // This is the next waypoint and it has a different Z level
                        Debugger($"[NAV] Next waypoint {checkIndex} requires floor change (Z={check.Z} vs current Z={currentZ})");

                        // Handle the floor change for the next waypoint
                        Coordinate transitionWaypoint = null;
                        int transitionIndex = -1;
                        int minTransitionDistance = int.MaxValue;

                        if (check.Z > currentZ)
                        {
                            // Need to go UP - find closest waypoint with higher Z
                            Debugger($"[NAV] Need to go UP to Z={check.Z}");
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
                            Debugger($"[NAV] Need to go DOWN to Z={check.Z}");
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
                            Debugger($"[NAV] Found transition waypoint: index {transitionIndex} at ({transitionWaypoint.X},{transitionWaypoint.Y},{transitionWaypoint.Z})");
                            lastTargetedIndex = checkIndex;
                            lastTargetedWaypoint = check;
                            return transitionWaypoint;
                        }
                        else
                        {
                            Debugger($"[NAV] No transition waypoint found - returning the next waypoint directly");
                            lastTargetedIndex = checkIndex;
                            lastTargetedWaypoint = check;
                            return check;
                        }
                    }
                    else
                    {
                        // This is not the next waypoint and has different Z - skip it entirely
                        Debugger($"[NAV] Skipping waypoint {checkIndex} - different Z level (Z={check.Z}) and not next waypoint");
                        continue;
                    }
                }

                // Calculate distance to this waypoint (same Z level as current)
                int distanceX = Math.Abs(check.X - currentX);
                int distanceY = Math.Abs(check.Y - currentY);
                int totalDistance = distanceX + distanceY;
                Debugger($"[NAV] Distance to waypoint {checkIndex}: dx={distanceX}, dy={distanceY}, total={totalDistance}");

                // Check if this waypoint is reachable
                if (distanceX <= MAX_DISTANCE && distanceY <= MAX_DISTANCE)
                {
                    // This waypoint is reachable, add it to our list
                    reachableWaypoints.Add((check, checkIndex, totalDistance));
                    Debugger($"[NAV] Reachable waypoint added: {checkIndex} at distance {totalDistance}");
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

                Debugger($"[NAV] Z-level checks: All top waypoints same Z={allTopSameZ}, Next 10 same Z={next10SameZ}");

                if (allTopSameZ && next10SameZ)
                {
                    // Randomly select from the top waypoints
                    Random rand = new Random();
                    int selectedIdx = rand.Next(topCount);
                    var selected = topWaypoints[selectedIdx];

                    selectedWaypoint = selected.waypoint;
                    selectedIndex = selected.index;
                    selectedDistance = selected.distance;

                    Debugger($"[NAV] Random selection enabled - Selected waypoint {selectedIndex} from top {topCount} reachable waypoints (distance={selectedDistance})");
                    Debugger($"[NAV] Available top waypoints were:");
                    for (int i = 0; i < topWaypoints.Count; i++)
                    {
                        var wp = topWaypoints[i];
                        Debugger($"[NAV]   {i}: Index {wp.index}, distance {wp.distance}");
                    }
                }
                else
                {
                    // Take the furthest waypoint (first in sorted list)
                    var selected = topWaypoints[0];
                    selectedWaypoint = selected.waypoint;
                    selectedIndex = selected.index;
                    selectedDistance = selected.distance;

                    Debugger($"[NAV] Z-level criteria not met - Selected furthest waypoint {selectedIndex} (distance={selectedDistance})");
                    if (!allTopSameZ)
                        Debugger($"[NAV] Reason: Not all top waypoints have same Z-level");
                    if (!next10SameZ)
                        Debugger($"[NAV] Reason: Next 10 waypoints don't all have same Z-level");
                }
            }

            // If still no waypoint found, create a directional step
            if (selectedWaypoint == null)
            {
                Debugger($"[NAV] No reachable waypoint found, calculating direction to next waypoint");

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

                Debugger($"[NAV] Returning directional step: ({selectedWaypoint.X},{selectedWaypoint.Y},{selectedWaypoint.Z}) toward waypoint {nextIndex}");
            }

            // Remember what we're targeting
            if (selectedIndex != currentCoordIndex)
            {
                lastTargetedIndex = selectedIndex;
                lastTargetedWaypoint = selectedWaypoint;
                Debugger($"[NAV] Targeting waypoint {selectedIndex} at ({selectedWaypoint.X},{selectedWaypoint.Y},{selectedWaypoint.Z})");
            }

            Debugger($"[NAV] Selected reachable waypoint: {selectedIndex} at ({selectedWaypoint.X},{selectedWaypoint.Y},{selectedWaypoint.Z}) distance={selectedDistance}");
            return selectedWaypoint;
        }

        static NavigationAction DetermineNextAction(List<Coordinate> waypoints, int currentX, int currentY, int currentZ)
        {
            Debugger($"[NAV] DetermineNextAction - Current position: ({currentX}, {currentY}, {currentZ})");
            Debugger($"[NAV] Current waypoint index: {currentCoordIndex}, Global last index: {globalLastIndex}");

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

            Debugger($"[NAV] Min distance to any waypoint: {minDistanceToAnyWaypoint}");
            Debugger($"[NAV] Was lost: {wasLost}, Is in recovery mode: {isInRecoveryMode}");

            // Handle recovery mode transitions
            if (wasLost && !isInRecoveryMode)
            {
                Debugger($"[NAV] CHARACTER IS LOST - entering recovery mode. Distance to nearest: {minDistanceToAnyWaypoint}");
                isInRecoveryMode = true;
                recoveryTarget = closestWaypoint;
                recoveryAttempts = 0;
                lastRecoveryTime = DateTime.Now;
            }
            else if (!wasLost && isInRecoveryMode)
            {
                Debugger("[NAV] Character recovered - exiting recovery mode");
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

            Debugger($"[NAV] Target: ({target.X}, {target.Y}, {target.Z})");
            Debugger($"[NAV] Distance to target: X={distanceX}, Y={distanceY}, Total={totalDistance}");

            if (distanceX == 0 && distanceY == 0)
            {
                Debugger($"[NAV] Already at target position");
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
                Debugger("[NAV] In recovery mode - analyzing movement options");

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
                        Debugger($"[NAV] Can now reach waypoint {i} - exiting recovery mode early");
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

                    Debugger($"[NAV] Recovery step to: ({stepTarget.X}, {stepTarget.Y}, {stepTarget.Z})");

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

                    Debugger($"[NAV] Using keyboard direction: {direction}");

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
                Debugger($"[NAV] Z-level change needed: {currentZ} -> {target.Z}");

                if (target.Z > currentZ)
                {
                    Debugger("[NAV] Need to go UP - using right-click");
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
                    Debugger("[NAV] Need to go DOWN - using arrow key");
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
                Debugger($"[NAV] Very close target - using keyboard (distance={totalDistance})");

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

                Debugger($"[NAV] Using keyboard direction: {direction}");

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
                Debugger($"[NAV] Close target - using keyboard (distance={totalDistance})");

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

                Debugger($"[NAV] Using keyboard direction: {direction}");

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
                Debugger($"[NAV] Far target - using click (distance={totalDistance})");

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
                    Debugger($"[WAYPOINT] Still close to last waypoint {globalLastIndex}, maintaining progression");
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

            Debugger($"[WAYPOINT] Searching waypoints from {searchStart} to {searchEnd} (current global: {globalLastIndex})");

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
                    Debugger($"[WAYPOINT] Preferring forward waypoint {i} (distance {distance})");
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
                Debugger($"[WAYPOINT] No waypoint found on Z-level {currentZ}, expanding search");

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
                Debugger($"[WARNING] No waypoint found in range, doing full search");

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
                Debugger("[WARNING] No waypoints found at all, returning index 0");
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
                    Debugger($"[WAYPOINT] Updating global last index from {globalLastIndex} to {closestIndex}");
                    globalLastIndex = closestIndex;
                }
                else
                {
                    Debugger($"[WAYPOINT] Not updating global index - would backtrack too far ({Math.Abs(closestIndex - globalLastIndex)} > {maxBacktrackDistance})");
                }
            }

            Debugger($"[WAYPOINT] Selected index {closestIndex} at ({waypoints[closestIndex].X}, {waypoints[closestIndex].Y}, {waypoints[closestIndex].Z}) distance {minDistance}");
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
            Debugger("[MONITOR] Soul and position monitor thread already running");
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
        Debugger("[MONITOR] Soul and position monitor thread started");
    }

    // Add this method to stop the monitoring thread
    static void StopSoulPositionMonitorThread()
    {
        soulPositionMonitorRunning = false;
        if (soulPositionMonitorThread != null && soulPositionMonitorThread.IsAlive)
        {
            soulPositionMonitorThread.Join(2000);
            Debugger("[MONITOR] Soul and position monitor thread stopped");
        }
    }

    // Add these additional P/Invoke declarations at the top with your other imports
    [DllImport("user32.dll")]
    static extern bool CloseWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    // Add this function to your Program class
    const int PROCESS_TERMINATE = 0x0001;

    // Add this function to your Program class
    static void ForceCloseGameProcessAndWindow()
    {
        try
        {
            Debugger("[CLOSE] Starting FORCEFUL game closure process...");

            // Step 1: Close the process handle if it exists
            if (processHandle != IntPtr.Zero)
            {
                Debugger("[CLOSE] Closing process handle...");
                CloseHandle(processHandle);
                processHandle = IntPtr.Zero;
            }

            // Step 2: Find and FORCE KILL all game processes immediately
            var processes = Process.GetProcesses()
                .Where(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        Debugger($"[CLOSE] FORCE KILLING process: {process.ProcessName} (ID: {process.Id})");

                        // Method 1: Try using Windows API TerminateProcess
                        try
                        {
                            IntPtr hProcess = OpenProcess(PROCESS_TERMINATE, false, process.Id);

                            if (hProcess != IntPtr.Zero)
                            {
                                bool terminated = TerminateProcess(hProcess, 1);
                                CloseHandle(hProcess);

                                if (terminated)
                                {
                                    Debugger($"[CLOSE] Process {process.Id} terminated via WinAPI");
                                    continue;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debugger($"[CLOSE] WinAPI termination failed: {ex.Message}");
                        }

                        // Method 2: Fallback to Process.Kill()
                        try
                        {
                            process.Kill();
                            Debugger($"[CLOSE] Process {process.Id} killed via Process.Kill()");
                        }
                        catch (Exception ex)
                        {
                            Debugger($"[CLOSE] Process.Kill() failed: {ex.Message}");
                        }

                        // Method 3: Alternative kill using command line (last resort)
                        try
                        {
                            ProcessStartInfo psi = new ProcessStartInfo();
                            psi.FileName = "taskkill";
                            psi.Arguments = $"/F /PID {process.Id}";
                            psi.WindowStyle = ProcessWindowStyle.Hidden;
                            psi.UseShellExecute = false;

                            Process killProcess = Process.Start(psi);
                            killProcess.WaitForExit(2000);

                            Debugger($"[CLOSE] Process {process.Id} killed via taskkill command");
                        }
                        catch (Exception ex)
                        {
                            Debugger($"[CLOSE] Taskkill failed: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debugger($"[CLOSE] Error force killing process {process.Id}: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        process.Dispose();
                    }
                    catch { }
                }
            }

            // Step 3: Force close any remaining windows
            if (targetWindow != IntPtr.Zero && IsWindow(targetWindow))
            {
                Debugger("[CLOSE] Force closing game window...");

                // Send WM_DESTROY to bypass confirmation dialogs
                SendMessage(targetWindow, 0x0002, IntPtr.Zero, IntPtr.Zero); // WM_DESTROY

                // Also try close window
                CloseWindow(targetWindow);
            }

            Debugger("[CLOSE] FORCEFUL game closure process completed");
        }
        catch (Exception ex)
        {
            Debugger($"[CLOSE] Error during forceful game closure: {ex.Message}");
        }
    }

    static void SoulPositionMonitorWorker()
    {
        Debugger("[MONITOR] Soul and position monitor worker started");

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
                            //Debugger($"[MONITOR] Soul changed to {curSoul:F1}");
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
                                //Debugger($"[MONITOR] Position changed from ({lastMonitoredPosition.X}, {lastMonitoredPosition.Y}, {lastMonitoredPosition.Z}) to ({currentX}, {currentY}, {currentZ})");
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
                            //Debugger($"[MONITOR] Status - Soul: {curSoul:F1} (unchanged for {timeSinceLastSoulChange:F1} sec), " +
                                            //$"Position: ({currentX}, {currentY}, {currentZ}) (unchanged for {timeSinceLastPositionChange:F1} sec)");
                        }
                    }

                    if (shouldShutdown)
                    {
                        Debugger($"\n[MONITOR] SHUTTING DOWN PROGRAM - {shutdownReason}");
                        Debugger("[MONITOR] This usually indicates the program is stuck or not functioning properly");

                        // FORCEFULLY close the game process and window before exiting
                        ForceCloseGameProcessAndWindow();

                        // Set the global flag to stop the program
                        programRunning = false;
                        cancellationTokenSource.Cancel();

                        // Stop other threads
                        StopMotionDetectionThread();
                        StopSoulPositionMonitorThread();

                        // Minimal delay before exit
                        Thread.Sleep(500);
                        Environment.Exit(0);
                    }

                    Thread.Sleep(MONITOR_INTERVAL_MS);
                }
                catch (Exception ex)
                {
                    Debugger($"[MONITOR] Error in monitoring loop: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }
        }
        catch (Exception ex)
        {
            Debugger($"[MONITOR] Fatal error in monitor thread: {ex.Message}");
        }
        finally
        {
            Debugger("[MONITOR] Soul and position monitor worker stopped");
        }
    }


    static void Debugger(string text)
    {
        Console.WriteLine($"[{DateTime.Now}] {text}");
    }
}
