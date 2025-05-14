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
    static int MANA_THRESHOLD = 880;
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
    // Improved Right-click action with verification similar to ArrowAction
    // Improved Right-click action with verification similar to ArrowAction
    public class RightClickAction : Action
    {
        public int DelayAfterMs { get; set; }
        public bool ExpectSpecificOutcome { get; set; }
        public bool ExpectZChange { get; set; } = true; // New property similar to ArrowAction
        public bool ExpectUpMovement { get; set; } = true; // Expect to go up (like stairs/ladders) = LOWER Z value

        public RightClickAction(int delayAfterMs = 100, bool expectSpecificOutcome = true, bool expectZChange = true, bool expectUpMovement = true)
        {
            DelayAfterMs = delayAfterMs;
            ExpectSpecificOutcome = expectSpecificOutcome;
            ExpectZChange = expectZChange;
            ExpectUpMovement = expectUpMovement;
            MaxRetries = 10; // Set higher retry count for Z-level changes
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

            // Right-click with retry logic similar to ArrowAction
            if (!ExpectZChange)
            {
                // Normal right-click without Z-level verification
                Debugger("Right-click without Z-level change verification");
                RightClickOnCharacter();
                return true;
            }

            // Z-level change verification logic (similar to ArrowAction)
            Debugger("Right-click with Z-level change verification");

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
                Debugger($"Attempt {attempt}/{maxAttempts} - Right-click with Z-level change");

                // Execute the right-click
                RightClickOnCharacter();

                // Wait for the action to complete
                Thread.Sleep(Math.Max(DelayAfterMs, 500)); // Minimum 500ms for Z-level changes

                // Check if Z-level changed
                ReadMemoryValues();
                bool zChanged = currentZ != originalZ;
                bool correctDirection = ExpectUpMovement ? currentZ < originalZ : currentZ > originalZ; // UP = LOWER Z

                // Check if both X and Y changed by more than 100 (teleportation-like movement)
                int xChange = Math.Abs(currentX - originalX);
                int yChange = Math.Abs(currentY - originalY);
                bool significantMovement = xChange > 100 && yChange > 100;

                if (significantMovement)
                {
                    Debugger($"Both X and Y changed by more than 100 - bypassing Z-level check");
                    Debugger($"X change: {xChange}, Y change: {yChange}");
                    return true;
                }

                Debugger($"After right-click: ({currentX}, {currentY}, {currentZ}) - Z changed: {zChanged}, Correct direction: {correctDirection}");
                Debugger($"Expected direction: {(ExpectUpMovement ? "UP (lower Z)" : "DOWN (higher Z)")}, Actual Z change: {originalZ} -> {currentZ}");

                if (zChanged && correctDirection)
                {
                    Debugger($"Success! Z-level changed from {originalZ} to {currentZ} in the expected direction");
                    return true;
                }
                else if (zChanged && !correctDirection)
                {
                    Debugger($"Z-level changed but in wrong direction. Expected {(ExpectUpMovement ? "UP (lower Z)" : "DOWN (higher Z)")} but went {(currentZ < originalZ ? "UP (lower Z)" : "DOWN (higher Z)")}");
                    // You might want to return to original position here, but for right-clicks this is usually not needed
                }

                // Z didn't change or changed in wrong direction - return to original position if we moved
                if (currentX != originalX || currentY != originalY)
                {
                    Debugger($"Z didn't change correctly but position moved. Returning to original position...");
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

            Debugger($"Failed to achieve Z-level change after {maxAttempts} attempts");
            return false;
        }

        public override bool VerifySuccess()
        {
            if (!ExpectZChange)
            {
                // For normal right-click actions, just wait the delay
                if (DelayAfterMs > 0)
                {
                    Thread.Sleep(DelayAfterMs);
                }
                return true;
            }

            // For Z-level changes, the verification is already done in Execute()
            // so we just return true here
            return true;
        }

        public override string GetDescription()
        {
            string baseDescription = "Right-click on character";
            if (ExpectZChange)
            {
                baseDescription += $" (with Z-level verification - expect {(ExpectUpMovement ? "UP (lower Z)" : "DOWN (higher Z)")})";
            }
            return baseDescription;
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
                    groundYLocal = groundY + 4*WAYPOINT_SIZE;
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
        public bool ExpectZChange { get; set; } = false; // For F12 Z-level verification
        public bool ExpectUpMovement { get; set; } = false; // Expect to go up (like stairs/ladders) = LOWER Z value
        public bool ExpectXChange { get; set; } = false; // For F7/F8 X coordinate verification
        public int MinXChange { get; set; } = 20; // Minimum X coordinate change expected

        public HotkeyAction(int keyCode, int delayMs = 100, bool expectZChange = false, bool expectUpMovement = false, bool expectXChange = false, int minXChange = 20)
        {
            KeyCode = keyCode;
            DelayMs = delayMs;
            ExpectZChange = expectZChange;
            ExpectUpMovement = expectUpMovement;
            ExpectXChange = expectXChange;
            MinXChange = minXChange;

            // Automatically set validation for specific keys
            if (keyCode == VK_F12)
            {
                ExpectZChange = true;
                ExpectUpMovement = true; // F12 (exani tera) typically moves up
                MaxRetries = 10; // Set higher retry count for Z-level changes
            }
            else if (keyCode == VK_F7 || keyCode == VK_F8)
            {
                ExpectXChange = true;
                MinXChange = minXChange;
                MaxRetries = 10; // Set higher retry count for teleportation spells
            }
        }

        public override bool Execute()
        {
            string keyName = GetKeyName(KeyCode);

            // Check if this key needs special validation
            bool needsValidation = (KeyCode == VK_F12 && ExpectZChange) ||
                                  ((KeyCode == VK_F7 || KeyCode == VK_F8) && ExpectXChange);

            if (!needsValidation)
            {
                // Normal hotkey press without validation
                Debugger($"Pressing {keyName}");
                SendKeyPress(KeyCode);
                return true;
            }

            // Special validation logic for F7, F8 (X-change) and F12 (Z-change)
            if (KeyCode == VK_F12)
            {
                return ExecuteF12WithZValidation(keyName);
            }
            else if (KeyCode == VK_F7 || KeyCode == VK_F8)
            {
                return ExecuteF7F8WithXValidation(keyName);
            }

            return false;
        }

        private bool ExecuteF12WithZValidation(string keyName)
        {
            Debugger($"{keyName} (exani tera) with Z-level change verification");

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
                Debugger($"Attempt {attempt}/{maxAttempts} - F12 with Z-level change");

                // Execute the F12 key press
                SendKeyPress(KeyCode);

                // Wait for the action to complete
                Thread.Sleep(Math.Max(DelayMs, 500)); // Minimum 500ms for Z-level changes

                // Check if Z-level changed
                ReadMemoryValues();
                bool zChanged = currentZ != originalZ;
                bool correctDirection = ExpectUpMovement ? currentZ < originalZ : currentZ > originalZ; // UP = LOWER Z

                // Check if both X and Y changed by more than 100 (teleportation-like movement)
                int xChange = Math.Abs(currentX - originalX);
                int yChange = Math.Abs(currentY - originalY);
                bool significantMovement = xChange > 100 && yChange > 100;

                if (significantMovement)
                {
                    Debugger($"Both X and Y changed by more than 100 - bypassing Z-level check");
                    Debugger($"X change: {xChange}, Y change: {yChange}");
                    return true;
                }

                Debugger($"After F12: ({currentX}, {currentY}, {currentZ}) - Z changed: {zChanged}, Correct direction: {correctDirection}");
                Debugger($"Expected direction: {(ExpectUpMovement ? "UP (lower Z)" : "DOWN (higher Z)")}, Actual Z change: {originalZ} -> {currentZ}");

                if (zChanged && correctDirection)
                {
                    Debugger($"Success! Z-level changed from {originalZ} to {currentZ} in the expected direction");
                    return true;
                }
                else if (zChanged && !correctDirection)
                {
                    Debugger($"Z-level changed but in wrong direction. Expected {(ExpectUpMovement ? "UP (lower Z)" : "DOWN (higher Z)")} but went {(currentZ < originalZ ? "UP (lower Z)" : "DOWN (higher Z)")}");
                }

                // Z didn't change or changed in wrong direction
                if (!zChanged)
                {
                    Debugger($"Z-level didn't change. Expected to go {(ExpectUpMovement ? "UP (lower Z)" : "DOWN (higher Z)")}");
                }

                attempt++;

                // Wait before next attempt
                if (attempt <= maxAttempts)
                {
                    Thread.Sleep(500);
                }
            }

            Debugger($"Failed to achieve Z-level change after {maxAttempts} attempts");
            return false;
        }

        private bool ExecuteF7F8WithXValidation(string keyName)
        {
            string spellName = KeyCode == VK_F7 ? "bring me to east" : "bring me to centre";
            Debugger($"{keyName} ({spellName}) with X coordinate change verification (min {MinXChange} squares)");

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
                Debugger($"Attempt {attempt}/{maxAttempts} - {keyName} with X coordinate change");

                // Execute the key press
                SendKeyPress(KeyCode);

                // Wait for the teleportation to complete
                Thread.Sleep(Math.Max(DelayMs, 1000)); // Minimum 1000ms for teleportation spells

                // Check if position changed
                ReadMemoryValues();
                int xChange = Math.Abs(currentX - originalX);
                int yChange = Math.Abs(currentY - originalY);
                int zChange = Math.Abs(currentZ - originalZ);

                Debugger($"After {keyName}: ({currentX}, {currentY}, {currentZ})");
                Debugger($"Position changes: X={xChange}, Y={yChange}, Z={zChange}");

                // Check if X coordinate changed by at least the minimum amount
                if (xChange >= MinXChange)
                {
                    Debugger($"Success! X coordinate changed by {xChange} squares (minimum {MinXChange} required)");
                    Debugger($"Teleported from ({originalX}, {originalY}, {originalZ}) to ({currentX}, {currentY}, {currentZ})");
                    return true;
                }

                // Also check for significant Y or Z changes (alternative success condition)
                bool significantMovement = yChange > 100 || zChange > 0;
                if (significantMovement)
                {
                    Debugger($"Success! Significant movement detected (Y change: {yChange}, Z change: {zChange})");
                    return true;
                }

                Debugger($"X coordinate only changed by {xChange} squares, but {MinXChange} was required");

                attempt++;

                // Wait before next attempt
                if (attempt <= maxAttempts)
                {
                    Thread.Sleep(500);
                }
            }

            Debugger($"Failed to achieve X coordinate change of at least {MinXChange} squares after {maxAttempts} attempts");
            return false;
        }

        public override bool VerifySuccess()
        {
            bool needsValidation = (KeyCode == VK_F12 && ExpectZChange) ||
                                  ((KeyCode == VK_F7 || KeyCode == VK_F8) && ExpectXChange);

            if (!needsValidation)
            {
                // For normal hotkey actions, just wait the delay
                if (DelayMs > 0)
                {
                    Thread.Sleep(DelayMs);
                }
                return true;
            }

            // For validated keys, the verification is already done in Execute()
            // so we just return true here
            return true;
        }

        public override string GetDescription()
        {
            string baseDescription = $"Press {GetKeyName(KeyCode)}";

            if (ExpectZChange && KeyCode == VK_F12)
            {
                baseDescription += $" (with Z-level verification - expect {(ExpectUpMovement ? "UP (lower Z)" : "DOWN (higher Z)")})";
            }
            else if (ExpectXChange && (KeyCode == VK_F7 || KeyCode == VK_F8))
            {
                string spellName = KeyCode == VK_F7 ? "bring me to east" : "bring me to centre";
                baseDescription += $" ({spellName} with X coordinate verification - min {MinXChange} squares)";
            }

            return baseDescription;
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

        public ArrowAction(ArrowDirection direction, int delayMs = 1000, bool expectZChange = true)
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
        actionSequence.Add(new MoveAction(32758, 32791, 8));
        actionSequence.Add(new HotkeyAction(VK_F12, 800)); //exani tera

        actionSequence.Add(new MoveAction(32758, 32793, 7));
        actionSequence.Add(new MoveAction(32754, 32790, 7));

        actionSequence.Add(new MoveAction(32757, 32788, 7));
        actionSequence.Add(new MoveAction(32757, 32784, 7));
        actionSequence.Add(new MoveAction(32755, 32780, 7));
        actionSequence.Add(new MoveAction(32751, 32777, 7));
        actionSequence.Add(new MoveAction(32746, 32774, 7));
        actionSequence.Add(new MoveAction(32741, 32771, 7));
        actionSequence.Add(new MoveAction(32735, 32770, 7));
        actionSequence.Add(new MoveAction(32730, 32773, 7));
        actionSequence.Add(new MoveAction(32725, 32775, 7));
        actionSequence.Add(new MoveAction(32721, 32775, 7));
        actionSequence.Add(new MoveAction(32716, 32775, 7));
        actionSequence.Add(new MoveAction(32710, 32775, 7));
        actionSequence.Add(new MoveAction(32703, 32776, 7));
        actionSequence.Add(new MoveAction(32697, 32777, 7));
        actionSequence.Add(new MoveAction(32691, 32777, 7));
        actionSequence.Add(new MoveAction(32685, 32775, 7));
        actionSequence.Add(new MoveAction(32681, 32776, 7));



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
        actionSequence.Clear();

        int baseX = 32597, baseY = 32747, baseZ = 7;

        for (int i = 0; i < 20; i++)
        {
            actionSequence.Add(new ScanBackpackAction());
        }
        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Down, 200));


        actionSequence.Add(new MoveAction(baseX + 0, baseY + 0, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 0, baseY + 5, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 7, baseY + 6, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 9, baseY + 11, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 15, baseY + 15, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 22, baseY + 15, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 24, baseY + 20, baseZ + 0));


        actionSequence.Add(new MoveAction(32624, 32769, 7));
        actionSequence.Add(new MoveAction(32631, 32769, 7));
        actionSequence.Add(new MoveAction(32638, 32769, 7));

        actionSequence.Add(new RightClickAction(200));

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



        actionSequence.Add(new MoveAction(32685, 32775, 7));
        actionSequence.Add(new MoveAction(32691, 32777, 7));
        actionSequence.Add(new MoveAction(32697, 32777, 7));
        actionSequence.Add(new MoveAction(32703, 32776, 7));
        actionSequence.Add(new MoveAction(32710, 32775, 7));
        actionSequence.Add(new MoveAction(32716, 32775, 7));
        actionSequence.Add(new MoveAction(32721, 32775, 7));
        actionSequence.Add(new MoveAction(32725, 32775, 7));
        actionSequence.Add(new MoveAction(32730, 32773, 7));
        actionSequence.Add(new MoveAction(32735, 32770, 7));
        actionSequence.Add(new MoveAction(32741, 32771, 7));
        actionSequence.Add(new MoveAction(32746, 32774, 7));
        actionSequence.Add(new MoveAction(32751, 32777, 7));
        actionSequence.Add(new MoveAction(32755, 32780, 7));
        actionSequence.Add(new MoveAction(32757, 32784, 7));
        actionSequence.Add(new MoveAction(32757, 32788, 7));

        actionSequence.Add(new MoveAction(32756, 32791, 7));
        actionSequence.Add(new MoveAction(32757, 32791, 7));


        actionSequence.Add(new ArrowAction(ArrowAction.ArrowDirection.Right, 200)); //down the hole

        ////HERE SHOULD FIGHT TARANTULAS UNTIL 200 SOUL
        actionSequence.Add(new FightTarantulasAction());


        actionSequence.Add(new MoveAction(32758, 32791, 8));
        actionSequence.Add(new HotkeyAction(VK_F12, 800)); //exani tera


        actionSequence.Add(new MoveAction(32758, 32793, 7));
        actionSequence.Add(new MoveAction(32754, 32790, 7));

        actionSequence.Add(new MoveAction(32757, 32788, 7));
        actionSequence.Add(new MoveAction(32757, 32784, 7));
        actionSequence.Add(new MoveAction(32755, 32780, 7));
        actionSequence.Add(new MoveAction(32751, 32777, 7));
        actionSequence.Add(new MoveAction(32746, 32774, 7));
        actionSequence.Add(new MoveAction(32741, 32771, 7));
        actionSequence.Add(new MoveAction(32735, 32770, 7));
        actionSequence.Add(new MoveAction(32730, 32773, 7));
        actionSequence.Add(new MoveAction(32725, 32775, 7));
        actionSequence.Add(new MoveAction(32721, 32775, 7));
        actionSequence.Add(new MoveAction(32716, 32775, 7));
        actionSequence.Add(new MoveAction(32710, 32775, 7));
        actionSequence.Add(new MoveAction(32703, 32776, 7));
        actionSequence.Add(new MoveAction(32697, 32777, 7));
        actionSequence.Add(new MoveAction(32691, 32777, 7));
        actionSequence.Add(new MoveAction(32685, 32775, 7));
        actionSequence.Add(new MoveAction(32681, 32776, 7));




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

        actionSequence.Add(new MoveAction(baseX + 0, baseY + 0, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 0, baseY + 5, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 7, baseY + 6, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 9, baseY + 11, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 15, baseY + 15, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 22, baseY + 15, baseZ + 0));
        actionSequence.Add(new MoveAction(baseX + 24, baseY + 20, baseZ + 0));


        actionSequence.Add(new MoveAction(32624, 32769, 7));
        actionSequence.Add(new MoveAction(32631, 32769, 7));
        actionSequence.Add(new MoveAction(32638, 32769, 7));

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
                // Check for pause before executing each action
                CheckForPause();

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
                    // Check for pause during retry loops
                    CheckForPause();

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
                            // Wait before retrying, but check for pause during the wait
                            for (int i = 0; i < 10; i++)
                            {
                                CheckForPause();
                                Thread.Sleep(100);
                            }
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

                // Small delay between actions, but check for pause
                for (int i = 0; i < 2; i++)
                {
                    CheckForPause();
                    Thread.Sleep(100);
                }
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
                                    Debugger("[MOTION] Utana vid successful - invisibility given");
                                }
                                else
                                {
                                    Debugger("[MOTION] Utana vid failed - still visible, will retry");
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

    static bool isPaused = false;
    static readonly object pauseLock = new object();
    static void QKeyListenerWorker()
    {
        Debugger("[Q-KEY] Q key listener worker started (Q=Quit, P=Pause/Resume)");

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
                        else if (key == ConsoleKey.P)
                        {
                            lock (pauseLock)
                            {
                                isPaused = !isPaused;
                                if (isPaused)
                                {
                                    Debugger("\n[PAUSE] Program PAUSED - press P again to resume");
                                }
                                else
                                {
                                    Debugger("\n[PAUSE] Program RESUMED");
                                }
                            }
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

    // Add this helper method to check for pause state
    static void CheckForPause()
    {
        while (isPaused && programRunning)
        {
            Thread.Sleep(100);
        }
    }

    static int f2ClickCount = 0;
    const int MAX_F2_CLICKS = 20;
    static DateTime lastF2AttemptTime = DateTime.MinValue;
    static double lastManaBeforeF2 = 0;
    static bool hasExecutedSequencesAfterF2Limit = false; // Flag to track if we've executed sequences after F2 limit


    static Process selectedProcess = null;
    static int selectedProcessId = -1;
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


        selectedProcess = process;
        selectedProcessId = process.Id;
        Debugger($"Selected process: {process.ProcessName} (ID: {selectedProcessId})");

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

            CheckForPause();
            ReadMemoryValues();
            try
            {
                // Check for quit key
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.E)
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
                }


                if (currentZ == 8)
                {
                    tarantulaSeqeunce();
                    ExecuteActionSequence();
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

                Debugger($"==========");
                Debugger($"f2ClickCount {f2ClickCount}");
                Debugger($"attempts: {attempts}");
                Debugger($"==========");

                Thread.Sleep(1000);

                if (f2ClickCount >= MAX_F2_CLICKS || curSoul <= 4 || (attempts >= 3 && curMana >= MANA_THRESHOLD)) {
                    attempts = 0;
                    if (curSoul >= 100)
                    {
                        InitializeMiddleSequence();
                        ExecuteActionSequence();
                        lastManaAction = DateTime.Now;
                        Debugger($"Mana>900 ({curMana:F0}), Soul>{SOUL_THRESHOLD} ({curSoul:F1}) - pressed F2");
                        lastManaAction = DateTime.Now;
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

    static int attempts = 0;
    static void PressF2WithValidation()
    {
        attempts++;
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
        Thread.Sleep(3000); // Give it a bit more time to register

        // Read mana after F2
        ReadMemoryValues();
        double manaAfterF2 = curMana;
        double manaDrop = manaBeforeF2 - manaAfterF2;

        // Validate the click
        if (manaDrop >= 500)
        {
            f2ClickCount++;
            Debugger($"[F2] F2 validated successfully! Mana dropped by {manaDrop:F0}. Total F2 clicks: {f2ClickCount}/{MAX_F2_CLICKS}");
            attempts = 0;
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
            Debugger("[CLOSE] Starting TARGETED game closure process...");

            // Step 1: Close the process handle if it exists
            if (processHandle != IntPtr.Zero)
            {
                Debugger("[CLOSE] Closing process handle...");
                CloseHandle(processHandle);
                processHandle = IntPtr.Zero;
            }

            // Step 2: Only terminate the SELECTED process
            if (selectedProcess != null && selectedProcessId > 0)
            {
                try
                {
                    // Check if the process still exists and matches our selection
                    Process currentProcess = null;
                    try
                    {
                        currentProcess = Process.GetProcessById(selectedProcessId);
                    }
                    catch (ArgumentException)
                    {
                        Debugger($"[CLOSE] Selected process {selectedProcessId} no longer exists");
                        return;
                    }

                    if (currentProcess != null && !currentProcess.HasExited)
                    {
                        Debugger($"[CLOSE] FORCE KILLING SELECTED process: {currentProcess.ProcessName} (ID: {selectedProcessId})");

                        // Method 1: Try using Windows API TerminateProcess
                        try
                        {
                            IntPtr hProcess = OpenProcess(PROCESS_TERMINATE, false, selectedProcessId);

                            if (hProcess != IntPtr.Zero)
                            {
                                bool terminated = TerminateProcess(hProcess, 1);
                                CloseHandle(hProcess);

                                if (terminated)
                                {
                                    Debugger($"[CLOSE] Selected process {selectedProcessId} terminated via WinAPI");
                                }
                                else
                                {
                                    throw new Exception("TerminateProcess returned false");
                                }
                            }
                            else
                            {
                                throw new Exception("Failed to open process for termination");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debugger($"[CLOSE] WinAPI termination failed: {ex.Message}");

                            // Method 2: Fallback to Process.Kill()
                            try
                            {
                                currentProcess.Kill();
                                Debugger($"[CLOSE] Selected process {selectedProcessId} killed via Process.Kill()");
                            }
                            catch (Exception ex2)
                            {
                                Debugger($"[CLOSE] Process.Kill() failed: {ex2.Message}");

                                // Method 3: Alternative kill using command line (last resort)
                                try
                                {
                                    ProcessStartInfo psi = new ProcessStartInfo();
                                    psi.FileName = "taskkill";
                                    psi.Arguments = $"/F /PID {selectedProcessId}";
                                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                                    psi.UseShellExecute = false;

                                    Process killProcess = Process.Start(psi);
                                    killProcess.WaitForExit(2000);

                                    Debugger($"[CLOSE] Selected process {selectedProcessId} killed via taskkill command");
                                }
                                catch (Exception ex3)
                                {
                                    Debugger($"[CLOSE] Taskkill failed: {ex3.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        Debugger($"[CLOSE] Selected process {selectedProcessId} has already exited");
                    }
                }
                catch (Exception ex)
                {
                    Debugger($"[CLOSE] Error force killing selected process {selectedProcessId}: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        selectedProcess?.Dispose();
                    }
                    catch { }
                }
            }
            else
            {
                Debugger("[CLOSE] No selected process to terminate");
            }

            // Step 3: Force close the associated window (if it exists)
            if (targetWindow != IntPtr.Zero && IsWindow(targetWindow))
            {
                Debugger("[CLOSE] Force closing associated game window...");

                // Send WM_DESTROY to bypass confirmation dialogs
                SendMessage(targetWindow, 0x0002, IntPtr.Zero, IntPtr.Zero); // WM_DESTROY

                // Also try close window
                CloseWindow(targetWindow);
            }

            Debugger("[CLOSE] TARGETED game closure process completed");
        }
        catch (Exception ex)
        {
            Debugger($"[CLOSE] Error during targeted game closure: {ex.Message}");
        }
    }

    // Optional: Add a method to safely dispose of the selected process reference
    static void DisposeSelectedProcess()
    {
        if (selectedProcess != null)
        {
            try
            {
                selectedProcess.Dispose();
            }
            catch (Exception ex)
            {
                Debugger($"[DISPOSE] Error disposing selected process: {ex.Message}");
            }
            finally
            {
                selectedProcess = null;
                selectedProcessId = -1;
            }
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
        Console.WriteLine($"[{DateTime.Now}] - (==SD BOT==)          {text}");
    }







    public class FightTarantulasAction : Action
    {
        private static CoordinateData loadedCoords;
        private static int currentCoordIndex = 0;
        private static string cordsFilePath = "cords.json";

        // Blocked waypoint tracking
        private static Dictionary<int, int> waypointFailureCount = new Dictionary<int, int>();
        private static HashSet<int> blockedWaypoints = new HashSet<int>();
        private const int MAX_WAYPOINT_FAILURES = 1;
        private const int BACKTRACK_ATTEMPTS = 3;

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
            ReturnToFirstWaypoint();
            return true;
        }

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
            }
            List<Coordinate> waypoints = loadedCoords.cords;

            // ALWAYS find the closest waypoint index first
            ReadMemoryValues();
            currentCoordIndex = FindClosestWaypointIndex(waypoints);
            Debugger($"[FIGHT] Starting fight from closest waypoint: {currentCoordIndex}");

            while (true)
            {
                CheckForPause();
                ReadMemoryValues();
                DoHealthManaChecks();
                if (curSoul >= 190)
                {
                    Debugger("[FIGHT] Soul limit reached, exiting fight");
                    return;
                }
                if (targetId == 0)
                {
                    SendKeyPress(VK_F6);
                    Thread.Sleep(150);
                    ReadMemoryValues();
                }
                if (targetId != 0)
                {
                    while (targetId != 0)
                    {
                        CheckForPause();
                        ReadMemoryValues();
                        DoHealthManaChecks();
                        Debugger($"[COMBAT] Fighting target {targetId}...");
                        Thread.Sleep(1000);
                        ReadMemoryValues();
                    }
                    Debugger("[COMBAT] Target killed!");
                }

                Thread.Sleep(200);
                ReadMemoryValues();
                if (targetId == 0)
                {
                    SendKeyPress(VK_F6);
                    Thread.Sleep(150);
                    ReadMemoryValues();
                }

                Thread.Sleep(200);
                ReadMemoryValues();

                if (targetId == 0)
                {
                    MoveToNextWaypoint(waypoints);
                    Thread.Sleep(100);
                }
            }
        }

        private void ReturnToFirstWaypoint()
        {
            if (loadedCoords == null || loadedCoords.cords.Count == 0)
            {
                Debugger("[RETURN] No waypoints loaded");
                return;
            }
            List<Coordinate> waypoints = loadedCoords.cords;
            Coordinate firstWaypoint = waypoints[0];

            // ALWAYS find the closest waypoint index first
            ReadMemoryValues();
            int actualCurrentIndex = FindClosestWaypointIndex(waypoints);
            Debugger($"[RETURN] Current position: ({currentX}, {currentY}, {currentZ})");
            Debugger($"[RETURN] Previous currentCoordIndex: {currentCoordIndex}, Actual closest waypoint: {actualCurrentIndex}");
            currentCoordIndex = actualCurrentIndex;

            if (IsAtPosition(firstWaypoint.X, firstWaypoint.Y, firstWaypoint.Z))
            {
                Debugger("[RETURN] Already at first waypoint");
                currentCoordIndex = 0;
                return;
            }
            Debugger($"[RETURN] Starting from waypoint {currentCoordIndex}, returning to waypoint 0");
            bool shouldGoBackward = DecideDirection(waypoints, currentCoordIndex);
            if (shouldGoBackward)
            {
                Debugger("[RETURN] Taking backward path (higher indices -> 0)");
                ReturnViaBackwardPath(waypoints);
            }
            else
            {
                Debugger("[RETURN] Taking forward path (current -> lower indices -> 0)");
                ReturnViaForwardPath(waypoints);
            }
            Debugger("[RETURN] Reached first waypoint");
            currentCoordIndex = 0;
        }

        private bool DecideDirection(List<Coordinate> waypoints, int fromIndex)
        {
            int forwardLength = fromIndex;
            int backwardLength = (waypoints.Count - fromIndex);
            bool useBackwardPath = backwardLength < forwardLength;
            Debugger($"[RETURN] Path analysis from waypoint {fromIndex}:");
            Debugger($"[RETURN] Forward path (direct): {forwardLength} steps ({fromIndex} -> 0)");
            Debugger($"[RETURN] Backward path (circular): {backwardLength} steps ({fromIndex} -> end -> 0)");
            Debugger($"[RETURN] Selected: {(useBackwardPath ? "BACKWARD (circular)" : "FORWARD (direct)")} path");
            return useBackwardPath;
        }

        private void ReturnViaForwardPath(List<Coordinate> waypoints)
        {
            // ALWAYS re-find current position at the start
            ReadMemoryValues();
            currentCoordIndex = FindClosestWaypointIndex(waypoints);
            Debugger($"[RETURN-FORWARD] Starting from actual closest waypoint: {currentCoordIndex}");

            while (currentCoordIndex > 0)
            {
                CheckForPause();
                ReadMemoryValues();
                DoHealthManaChecks();

                // Re-find closest waypoint if we might have moved
                int actualCurrentIndex = FindClosestWaypointIndex(waypoints);
                if (actualCurrentIndex != currentCoordIndex)
                {
                    Debugger($"[RETURN-FORWARD] Position adjusted: {currentCoordIndex} -> {actualCurrentIndex}");
                    currentCoordIndex = actualCurrentIndex;
                    if (currentCoordIndex == 0) break; // Already at destination
                }

                Coordinate targetWaypoint = null;
                int targetIndex = currentCoordIndex - 1;
                int maxJump = 0;
                for (int steps = 1; steps <= Math.Min(15, currentCoordIndex); steps++)
                {
                    int candidateIndex = currentCoordIndex - steps;
                    Coordinate candidate = waypoints[candidateIndex];
                    int distanceX = Math.Abs(candidate.X - currentX);
                    int distanceY = Math.Abs(candidate.Y - currentY);
                    if (distanceX <= 5 && distanceY <= 5 && candidate.Z == currentZ)
                    {
                        targetWaypoint = candidate;
                        targetIndex = candidateIndex;
                        maxJump = steps;
                    }
                }
                if (targetWaypoint == null)
                {
                    targetWaypoint = waypoints[currentCoordIndex - 1];
                    targetIndex = currentCoordIndex - 1;
                    maxJump = 1;
                }

                bool moveSuccessful = AttemptMoveWithBlocking(targetWaypoint, targetIndex, waypoints);

                if (moveSuccessful)
                {
                    currentCoordIndex = targetIndex;
                    Debugger($"[RETURN-FORWARD] Forward jump: {maxJump} waypoints to waypoint {targetIndex}");
                }
                else
                {
                    // If move failed, re-find closest waypoint
                    currentCoordIndex = FindClosestWaypointIndex(waypoints);
                    Debugger($"[RETURN-FORWARD] Move failed, updated to closest waypoint: {currentCoordIndex}");
                }
            }
        }

        private void ReturnViaBackwardPath(List<Coordinate> waypoints)
        {
            // ALWAYS re-find current position at the start
            ReadMemoryValues();
            currentCoordIndex = FindClosestWaypointIndex(waypoints);
            Debugger($"[RETURN-BACKWARD] Starting from actual closest waypoint: {currentCoordIndex}");

            while (currentCoordIndex < waypoints.Count - 1)
            {
                CheckForPause();
                ReadMemoryValues();
                DoHealthManaChecks();

                // Re-find closest waypoint if we might have moved
                int actualCurrentIndex = FindClosestWaypointIndex(waypoints);
                if (actualCurrentIndex != currentCoordIndex)
                {
                    Debugger($"[RETURN-BACKWARD] Position adjusted: {currentCoordIndex} -> {actualCurrentIndex}");
                    currentCoordIndex = actualCurrentIndex;
                }

                Coordinate targetWaypoint = null;
                int targetIndex = currentCoordIndex + 1;
                int maxJump = 0;
                int maxSteps = Math.Min(15, waypoints.Count - 1 - currentCoordIndex);
                for (int steps = 1; steps <= maxSteps; steps++)
                {
                    int candidateIndex = currentCoordIndex + steps;
                    Coordinate candidate = waypoints[candidateIndex];
                    int distanceXx = Math.Abs(candidate.X - currentX);
                    int distanceYy = Math.Abs(candidate.Y - currentY);
                    if (distanceXx <= 5 && distanceYy <= 5 && candidate.Z == currentZ)
                    {
                        targetWaypoint = candidate;
                        targetIndex = candidateIndex;
                        maxJump = steps;
                    }
                }
                if (targetWaypoint == null)
                {
                    targetWaypoint = waypoints[currentCoordIndex + 1];
                    targetIndex = currentCoordIndex + 1;
                    maxJump = 1;
                }

                bool moveSuccessful = AttemptMoveWithBlocking(targetWaypoint, targetIndex, waypoints);

                if (moveSuccessful)
                {
                    currentCoordIndex = targetIndex;
                    Debugger($"[RETURN-BACKWARD] Backward jump: {maxJump} waypoints to waypoint {targetIndex}");
                }
                else
                {
                    // If move failed, re-find closest waypoint
                    currentCoordIndex = FindClosestWaypointIndex(waypoints);
                    Debugger($"[RETURN-BACKWARD] Move failed, updated to closest waypoint: {currentCoordIndex}");
                }
            }

            // Final jump to waypoint 0
            CheckForPause();
            ReadMemoryValues();
            DoHealthManaChecks();
            Coordinate firstWaypoint = waypoints[0];
            int distanceX = Math.Abs(firstWaypoint.X - currentX);
            int distanceY = Math.Abs(firstWaypoint.Y - currentY);
            if (distanceX <= 5 && distanceY <= 5 && firstWaypoint.Z == currentZ)
            {
                bool moveSuccessful = AttemptMoveWithBlocking(firstWaypoint, 0, waypoints);

                if (moveSuccessful)
                {
                    currentCoordIndex = 0;
                    Debugger("[RETURN-BACKWARD] Direct jump from end to waypoint 0 (exact position)");
                }
                else
                {
                    Debugger("[RETURN-BACKWARD] Failed direct jump to waypoint 0, using step-by-step approach");
                    // Re-find closest and use forward path
                    currentCoordIndex = FindClosestWaypointIndex(waypoints);
                    ReturnViaForwardPath(waypoints);
                }
            }
            else
            {
                Debugger($"[RETURN-BACKWARD] Waypoint 0 too far from end (X:{distanceX}, Y:{distanceY}), using step-by-step approach");
                // Re-find closest and use forward path
                currentCoordIndex = FindClosestWaypointIndex(waypoints);
                ReturnViaForwardPath(waypoints);
            }
        }

        private void DoHealthManaChecks()
        {
            double hpPercent = (curHP / maxHP) * 100;
            double mana = curMana;
            if (hpPercent <= HP_THRESHOLD)
            {
                if ((DateTime.Now - lastHpAction).TotalMilliseconds >= 2000)
                {
                    SendKeyPress(VK_F1);
                    lastHpAction = DateTime.Now;
                    Debugger($"HP low ({hpPercent:F1}%) - pressed F1");
                }
            }
            if (mana <= MANA_THRESHOLD)
            {
                if ((DateTime.Now - lastManaAction).TotalMilliseconds >= 2000)
                {
                    SendKeyPress(VK_F3);
                    lastManaAction = DateTime.Now;
                    Debugger($"Mana low ({mana:F1}) - pressed F3");
                }
            }
        }

        private void MoveToNextWaypoint(List<Coordinate> waypoints)
        {
            ReadMemoryValues();

            // ALWAYS find the closest waypoint index first, regardless of currentCoordIndex
            int actualCurrentIndex = FindClosestWaypointIndex(waypoints);
            Debugger($"[MOVE] Current position: ({currentX}, {currentY}, {currentZ})");
            Debugger($"[MOVE] Previous currentCoordIndex: {currentCoordIndex}, Actual closest waypoint: {actualCurrentIndex}");

            // Update currentCoordIndex to the actual closest waypoint
            currentCoordIndex = actualCurrentIndex;

            int nextIndex = (currentCoordIndex + 1) % waypoints.Count;
            Coordinate bestTarget = null;
            int bestIndex = nextIndex;
            int maxProgress = 0;

            // Skip blocked waypoints when looking for next waypoint
            for (int i = 1; i <= 15; i++)
            {
                int checkIndex = (currentCoordIndex + i) % waypoints.Count;

                // Skip if this waypoint is blocked
                if (blockedWaypoints.Contains(checkIndex))
                {
                    Debugger($"[MOVE] Skipping blocked waypoint {checkIndex}");
                    continue;
                }

                Coordinate candidate = waypoints[checkIndex];
                int distanceX = Math.Abs(candidate.X - currentX);
                int distanceY = Math.Abs(candidate.Y - currentY);
                if (distanceX <= 5 && distanceY <= 5 && candidate.Z == currentZ)
                {
                    if (i > maxProgress)
                    {
                        bestTarget = candidate;
                        bestIndex = checkIndex;
                        maxProgress = i;
                        Debugger($"[MOVE] Found reachable waypoint {checkIndex} at {i} steps ahead");
                    }
                }
            }

            if (bestTarget == null)
            {
                Debugger("[MOVE] No reachable waypoint found ahead, trying backward direction");
                for (int i = 1; i <= 15; i++)
                {
                    int checkIndex = (currentCoordIndex - i + waypoints.Count) % waypoints.Count;

                    // Skip if this waypoint is blocked
                    if (blockedWaypoints.Contains(checkIndex))
                    {
                        Debugger($"[MOVE] Skipping blocked waypoint {checkIndex} (backward)");
                        continue;
                    }

                    Coordinate candidate = waypoints[checkIndex];
                    int distanceX = Math.Abs(candidate.X - currentX);
                    int distanceY = Math.Abs(candidate.Y - currentY);
                    if (distanceX <= 5 && distanceY <= 5 && candidate.Z == currentZ)
                    {
                        bestTarget = candidate;
                        bestIndex = checkIndex;
                        Debugger($"[MOVE] Found reachable waypoint behind: {checkIndex}");
                        break;
                    }
                }
            }

            if (bestTarget == null)
            {
                Debugger("[MOVE] No reachable waypoint in either direction, using next sequential waypoint");
                nextIndex = (currentCoordIndex + 1) % waypoints.Count;
                bestTarget = waypoints[nextIndex];
                bestIndex = nextIndex;
            }

            bool moveSuccessful = AttemptMoveWithBlocking(bestTarget, bestIndex, waypoints);

            if (moveSuccessful)
            {
                currentCoordIndex = bestIndex;
                Debugger($"[MOVE] Moved to waypoint {bestIndex} ({bestTarget.X}, {bestTarget.Y}, {bestTarget.Z}) - {maxProgress} steps ahead");
            }
            else
            {
                Debugger($"[MOVE] Failed to move to waypoint {bestIndex} after all attempts");
                // Re-find closest waypoint after failed move
                currentCoordIndex = FindClosestWaypointIndex(waypoints);
                Debugger($"[MOVE] Updated currentCoordIndex to closest waypoint: {currentCoordIndex}");
            }
        }

        private bool AttemptMoveWithBlocking(Coordinate targetWaypoint, int targetIndex, List<Coordinate> waypoints)
        {
            // Check if waypoint is already marked as blocked
            if (blockedWaypoints.Contains(targetIndex))
            {
                Debugger($"[BLOCKING] Waypoint {targetIndex} is marked as blocked, skipping");
                return false;
            }

            var moveAction = new MoveAction(targetWaypoint.X, targetWaypoint.Y, targetWaypoint.Z);
            bool moveSuccessful = false;

            for (int attempt = 1; attempt <= moveAction.MaxRetries; attempt++)
            {
                if (moveAction.Execute() && moveAction.VerifySuccess())
                {
                    // Clear all blocked waypoints on successful move - character position changed
                    ResetBlockedWaypoints();
                    Debugger($"[BLOCKING] Successful move - cleared all blocked waypoints");
                    moveSuccessful = true;
                    break;
                }
                else
                {
                    Debugger($"[MOVE] Attempt {attempt}: Failed to move to waypoint {targetIndex}");
                }

                if (attempt < moveAction.MaxRetries)
                {
                    Thread.Sleep(128);
                }
            }

            // Handle failure tracking
            if (!moveSuccessful)
            {
                // Increment failure count for this waypoint
                if (!waypointFailureCount.ContainsKey(targetIndex))
                {
                    waypointFailureCount[targetIndex] = 0;
                }
                waypointFailureCount[targetIndex]++;

                Debugger($"[BLOCKING] Waypoint {targetIndex} failed {waypointFailureCount[targetIndex]} times");

                // Check if waypoint should be marked as blocked
                if (waypointFailureCount[targetIndex] >= MAX_WAYPOINT_FAILURES)
                {
                    blockedWaypoints.Add(targetIndex);
                    Debugger($"[BLOCKING] Waypoint {targetIndex} marked as BLOCKED after {MAX_WAYPOINT_FAILURES} failures");

                    // Attempt to backtrack and retry
                    if (HandleBlockedWaypoint(waypoints, targetIndex))
                    {
                        return true; // Successfully handled the blocking
                    }
                }

                Thread.Sleep(300);
            }

            return moveSuccessful;
        }

        private bool HandleBlockedWaypoint(List<Coordinate> waypoints, int blockedIndex)
        {
            Debugger($"[BLOCKING] Handling blocked waypoint {blockedIndex}, attempting backtrack");

            // ALWAYS re-find current position before backtracking
            ReadMemoryValues();
            int actualCurrentIndex = FindClosestWaypointIndex(waypoints);
            if (actualCurrentIndex != currentCoordIndex)
            {
                Debugger($"[BLOCKING] Position adjusted before backtrack: {currentCoordIndex} -> {actualCurrentIndex}");
                currentCoordIndex = actualCurrentIndex;
            }

            // Find previous non-blocked waypoint
            int backtrackIndex = currentCoordIndex;
            for (int i = 1; i <= BACKTRACK_ATTEMPTS; i++)
            {
                backtrackIndex = (currentCoordIndex - i + waypoints.Count) % waypoints.Count;

                if (!blockedWaypoints.Contains(backtrackIndex))
                {
                    Debugger($"[BLOCKING] Backtracking to waypoint {backtrackIndex}");

                    // Move to previous waypoint
                    Coordinate backtrackTarget = waypoints[backtrackIndex];
                    var backtrackMove = new MoveAction(backtrackTarget.X, backtrackTarget.Y, backtrackTarget.Z);

                    for (int attempt = 1; attempt <= backtrackMove.MaxRetries; attempt++)
                    {
                        if (backtrackMove.Execute() && backtrackMove.VerifySuccess())
                        {
                            currentCoordIndex = backtrackIndex;
                            Debugger($"[BLOCKING] Successfully backtracked to waypoint {backtrackIndex}");

                            // Clear all blocked waypoints since character moved
                            ResetBlockedWaypoints();
                            Debugger("[BLOCKING] Cleared all blocked waypoints after backtrack");

                            // Wait a moment then continue
                            Thread.Sleep(1000);
                            return true;
                        }

                        if (attempt < backtrackMove.MaxRetries)
                        {
                            Thread.Sleep(500);
                        }
                    }
                }
            }

            Debugger("[BLOCKING] Failed to backtrack, continuing with closest waypoint");
            currentCoordIndex = FindClosestWaypointIndex(waypoints);
            return false;
        }

        private int FindClosestWaypointIndex(List<Coordinate> waypoints)
        {
            ReadMemoryValues();
            int closestIndex = -1;
            int minDistance = int.MaxValue;
            bool foundOnSameZ = false;

            // First pass: Find closest waypoint on the same Z level
            for (int i = 0; i < waypoints.Count; i++)
            {
                // Skip blocked waypoints when finding closest
                if (blockedWaypoints.Contains(i))
                {
                    continue;
                }

                var waypoint = waypoints[i];

                // Calculate Manhattan distance (X + Y distance)
                int distance = Math.Abs(waypoint.X - currentX) + Math.Abs(waypoint.Y - currentY);

                // Prioritize waypoints on the same Z level
                if (waypoint.Z == currentZ)
                {
                    if (!foundOnSameZ || distance < minDistance)
                    {
                        minDistance = distance;
                        closestIndex = i;
                        foundOnSameZ = true;
                    }
                }
                // Only consider different Z levels if no same-Z waypoint found yet
                else if (!foundOnSameZ && distance < minDistance)
                {
                    minDistance = distance;
                    closestIndex = i;
                }
            }

            // Fallback: If all waypoints are blocked, find closest regardless of blocking
            if (closestIndex == -1)
            {
                Debugger("[FIND_CLOSEST] All waypoints blocked, finding closest regardless of blocking status");
                minDistance = int.MaxValue;
                foundOnSameZ = false;

                for (int i = 0; i < waypoints.Count; i++)
                {
                    var waypoint = waypoints[i];
                    int distance = Math.Abs(waypoint.X - currentX) + Math.Abs(waypoint.Y - currentY);

                    // Prioritize waypoints on the same Z level
                    if (waypoint.Z == currentZ)
                    {
                        if (!foundOnSameZ || distance < minDistance)
                        {
                            minDistance = distance;
                            closestIndex = i;
                            foundOnSameZ = true;
                        }
                    }
                    // Only consider different Z levels if no same-Z waypoint found yet
                    else if (!foundOnSameZ && distance < minDistance)
                    {
                        minDistance = distance;
                        closestIndex = i;
                    }
                }
            }

            // Final fallback: If still no waypoint found, return 0
            if (closestIndex == -1)
            {
                Debugger("[FIND_CLOSEST] No valid waypoint found, defaulting to waypoint 0");
                closestIndex = 0;
            }

            var selectedWaypoint = waypoints[closestIndex];
            Debugger($"[FIND_CLOSEST] Character at ({currentX}, {currentY}, {currentZ}) -> Closest waypoint {closestIndex} at ({selectedWaypoint.X}, {selectedWaypoint.Y}, {selectedWaypoint.Z}) - Distance: {minDistance}");

            return closestIndex;
        }

        // Optional: Method to reset blocked waypoints (useful for debugging or manual resets)
        public static void ResetBlockedWaypoints()
        {
            blockedWaypoints.Clear();
            waypointFailureCount.Clear();
            Debugger("[BLOCKING] Reset all blocked waypoints");
        }

        // Optional: Method to manually unblock a specific waypoint
        public static void UnblockWaypoint(int index)
        {
            if (blockedWaypoints.Remove(index))
            {
                waypointFailureCount.Remove(index);
                Debugger($"[BLOCKING] Manually unblocked waypoint {index}");
            }
        }
    }


}
