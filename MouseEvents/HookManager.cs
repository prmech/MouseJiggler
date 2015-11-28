// Adapted from George Mamaladze, http://www.codeproject.com/Articles/7294/Processing-Global-Mouse-and-Keyboard-Hooks-in-C
// Modified & copyrighted: Copyright Patrick Mechenbier 2015.

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseJiggle.MouseEvents
{
    /// <summary>This class monitors all mouse activities globally (also outside of the application) and provides appropriate events.</summary>
    public static partial class HookManager
    {
        //values from Winuser.h in Microsoft SDK.
        /// <summary>Windows NT/2000/XP: Installs a hook procedure that monitors low-level mouse input events.</summary>
        private const int WH_MOUSE_LL = 14;

        /// <summary>Stores the handle to the mouse hook procedure.</summary>
        private static int s_MouseHookHandle;
        private static int m_OldX;
        private static int m_OldY;

        private static event MouseEventHandler s_MouseMove;
        /// <summary>Occurs when the mouse pointer is moved.</summary>
        public static event MouseEventHandler MouseMove
        {
            add
            {
                EnsureSubscribedToGlobalMouseEvents();
                s_MouseMove += value;
            }

            remove
            {
                s_MouseMove -= value;
                TryUnsubscribeFromGlobalMouseEvents();
            }
        }

        /// <summary>The CallWndProc hook procedure is an application-defined or library-defined callback function used with the SetWindowsHookEx function. The HOOKPROC type defines a pointer 
        /// to this callback function. CallWndProc is a placeholder for the application-defined or library-defined function name.</summary>
        /// <param name="nCode">Specifies whether the hook procedure must process the message. If nCode is HC_ACTION, the hook procedure must process the message. 
        /// If nCode is less than zero, the hook procedure must pass the message to the CallNextHookEx function without further processing and must return the value returned by CallNextHookEx.</param>
        /// <param name="wParam">Specifies whether the message was sent by the current thread. If the message was sent by the current thread, it is nonzero; otherwise, it is zero.</param>
        /// <param name="lParam">Pointer to a CWPSTRUCT structure that contains details about the message.</param>
        /// <returns>If nCode is less than zero, the hook procedure must return the value returned by CallNextHookEx. If nCode is greater than or equal to zero, it is highly recommended that you call CallNextHookEx 
        /// and return the value it returns; otherwise, other applications that have installed WH_CALLWNDPROC hooks will not receive hook notifications and may behave incorrectly as a result. If the hook 
        /// procedure does not call CallNextHookEx, the return value should be zero.</returns>
        /// <remarks>http://msdn.microsoft.com/library/default.asp?url=/library/en-us/winui/winui/windowsuserinterface/windowing/hooks/hookreference/hookfunctions/callwndproc.asp</remarks>
        private delegate int HookProc(int nCode, int wParam, IntPtr lParam);

        /// <summary>This field is not objectively needed but we need to keep a reference on a delegate which will be passed to unmanaged code. To avoid GC to clean it up.
        /// When passing delegates to unmanaged code, they must be kept alive by the managed application until it is guaranteed that they will never be called.</summary>
        private static HookProc s_MouseDelegate;

        /// <summary>A callback function which will be called every Time a mouse activity detected.</summary>
        /// <param name="nCode">Specifies whether the hook procedure must process the message. If nCode is HC_ACTION, the hook procedure must process the message. 
        /// If nCode is less than zero, the hook procedure must pass the message to the CallNextHookEx function without further processing and must return the value returned by CallNextHookEx.</param>
        /// <param name="wParam">Specifies whether the message was sent by the current thread. If the message was sent by the current thread, it is nonzero; otherwise, it is zero.</param>       
        /// <param name="lParam">Pointer to a CWPSTRUCT structure that contains details about the message.</param>
        /// <returns>If nCode is less than zero, the hook procedure must return the value returned by CallNextHookEx. If nCode is greater than or equal to zero, it is highly recommended that you call CallNextHookEx 
        /// and return the value it returns; otherwise, other applications that have installed WH_CALLWNDPROC hooks will not receive hook notifications and may behave incorrectly as a result. If the hook 
        /// procedure does not call CallNextHookEx, the return value should be zero.</returns>
        private static int MouseHookProc(int nCode, int wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                //Marshall the data from callback.
                MouseLLHookStruct mouseHookStruct = (MouseLLHookStruct)Marshal.PtrToStructure(lParam, typeof(MouseLLHookStruct));

                //detect button clicked
                MouseButtons button = MouseButtons.None;
                short mouseDelta = 0;
                int clickCount = 0;

                //generate event 
                MouseEventExtArgs e = new MouseEventExtArgs(button, clickCount, mouseHookStruct.Point.X, mouseHookStruct.Point.Y, mouseDelta);

                //If someone listens to move and there was a change in coordinates raise move event
                if ((s_MouseMove != null) && (m_OldX != mouseHookStruct.Point.X || m_OldY != mouseHookStruct.Point.Y))
                {
                    m_OldX = mouseHookStruct.Point.X;
                    m_OldY = mouseHookStruct.Point.Y;
                    if (s_MouseMove != null)
                    {
                        s_MouseMove.Invoke(null, e);
                    }
                }

                if (e.Handled)
                {
                    return -1;
                }
            }

            //call next hook
            return CallNextHookEx(s_MouseHookHandle, nCode, wParam, lParam);
        }

        private static void EnsureSubscribedToGlobalMouseEvents()
        {
            // install Mouse hook only if it is not installed and must be installed
            if (s_MouseHookHandle == 0)
            {
                //See comment of this field. To avoid GC to clean it up.
                s_MouseDelegate = MouseHookProc;
                //install hook
                s_MouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, s_MouseDelegate, Marshal.GetHINSTANCE(System.Reflection.Assembly.GetExecutingAssembly().GetModules()[0]), 0);
                //If SetWindowsHookEx fails.
                if (s_MouseHookHandle == 0)
                {
                    //Returns the error code returned by the last unmanaged function called using platform invoke that has the DllImportAttribute.SetLastError flag set. 
                    int errorCode = Marshal.GetLastWin32Error();
                    //do cleanup

                    //Initializes and throws a new instance of the Win32Exception class with the specified error. 
                    throw new Win32Exception(errorCode);
                }
            }
        }

        private static void TryUnsubscribeFromGlobalMouseEvents()
        {
            //if no subsribers are registered unsubsribe from hook
            if (s_MouseMove == null)
            {
                ForceUnsunscribeFromGlobalMouseEvents();
            }
        }

        private static void ForceUnsunscribeFromGlobalMouseEvents()
        {
            if (s_MouseHookHandle != 0)
            {
                //uninstall hook
                int result = UnhookWindowsHookEx(s_MouseHookHandle);
                //reset invalid handle
                s_MouseHookHandle = 0;
                //Free up for GC
                s_MouseDelegate = null;
                //if failed and exception must be thrown
                if (result == 0)
                {
                    //Returns the error code returned by the last unmanaged function called using platform invoke that has the DllImportAttribute.SetLastError flag set. 
                    int errorCode = Marshal.GetLastWin32Error();
                    //Initializes and throws a new instance of the Win32Exception class with the specified error. 
                    throw new Win32Exception(errorCode);
                }
            }
        }

        #region Dll Imports
        /// <summary>The CallNextHookEx function passes the hook information to the next hook procedure in the current hook chain. A hook procedure can call this function either before or after processing the hook information.</summary>
        /// <param name="idHook">Ignored.</param>
        /// <param name="nCode">Specifies the hook code passed to the current hook procedure. The next hook procedure uses this code to determine how to process the hook information.</param>
        /// <param name="wParam">Specifies the wParam value passed to the current hook procedure. The meaning of this parameter depends on the type of hook associated with the current hook chain.</param>
        /// <param name="lParam">Specifies the lParam value passed to the current hook procedure. The meaning of this parameter depends on the type of hook associated with the current hook chain.</param>
        /// <returns>This value is returned by the next hook procedure in the chain. The current hook procedure must also return this value. The meaning of the return value depends on the hook type. For more information, see the descriptions of the individual hook procedures.</returns>
        /// <remarks>http://msdn.microsoft.com/library/default.asp?url=/library/en-us/winui/winui/windowsuserinterface/windowing/hooks/hookreference/hookfunctions/setwindowshookex.asp</remarks>
        [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
        private static extern int CallNextHookEx(int idHook, int nCode, int wParam, IntPtr lParam);

        /// <summary>
        /// The SetWindowsHookEx function installs an application-defined hook procedure into a hook chain. 
        /// You would install a hook procedure to monitor the system for certain types of events. These events 
        /// are associated either with a specific thread or with all threads in the same desktop as the calling thread. 
        /// </summary>
        /// <param name="idHook">
        /// [in] Specifies the type of hook procedure to be installed. This parameter can be one of the following values.
        /// </param>
        /// <param name="lpfn">
        /// [in] Pointer to the hook procedure. If the dwThreadId parameter is zero or specifies the identifier of a 
        /// thread created by a different process, the lpfn parameter must point to a hook procedure in a dynamic-link 
        /// library (DLL). Otherwise, lpfn can point to a hook procedure in the code associated with the current process.
        /// </param>
        /// <param name="hMod">
        /// [in] Handle to the DLL containing the hook procedure pointed to by the lpfn parameter. 
        /// The hMod parameter must be set to NULL if the dwThreadId parameter specifies a thread created by 
        /// the current process and if the hook procedure is within the code associated with the current process. 
        /// </param>
        /// <param name="dwThreadId">
        /// [in] Specifies the identifier of the thread with which the hook procedure is to be associated. 
        /// If this parameter is zero, the hook procedure is associated with all existing threads running in the 
        /// same desktop as the calling thread. 
        /// </param>
        /// <returns>
        /// If the function succeeds, the return value is the handle to the hook procedure.
        /// If the function fails, the return value is NULL. To get extended error information, call GetLastError.
        /// </returns>
        /// <remarks>
        /// http://msdn.microsoft.com/library/default.asp?url=/library/en-us/winui/winui/windowsuserinterface/windowing/hooks/hookreference/hookfunctions/setwindowshookex.asp
        /// </remarks>
        [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
        private static extern int SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, int dwThreadId);

        /// <summary>The UnhookWindowsHookEx function removes a hook procedure installed in a hook chain by the SetWindowsHookEx function.</summary>
        /// <param name="idHook">Handle to the hook to be removed. This parameter is a hook handle obtained by a previous call to SetWindowsHookEx.</param>
        /// <returns>If the function succeeds, the return value is nonzero. If the function fails, the return value is zero. To get extended error information, call GetLastError.</returns>
        /// <remarks>http://msdn.microsoft.com/library/default.asp?url=/library/en-us/winui/winui/windowsuserinterface/windowing/hooks/hookreference/hookfunctions/setwindowshookex.asp</remarks>
        [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
        private static extern int UnhookWindowsHookEx(int idHook);
        #endregion

        #region Structs
        /// <summary>The Point structure defines the X- and Y- coordinates of a point.</summary>
        /// <remarks>http://msdn.microsoft.com/library/default.asp?url=/library/en-us/gdi/rectangl_0tiq.asp</remarks>
        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            /// <summary>Specifies the X-coordinate of the point.</summary>
            public int X;
            /// <summary>Specifies the Y-coordinate of the point.</summary>
            public int Y;
        }

        /// <summary>The MSLLHOOKSTRUCT structure contains information about a low-level keyboard input event.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseLLHookStruct
        {
            /// <summary>Specifies a Point structure that contains the X- and Y-coordinates of the cursor, in screen coordinates.</summary>
            public Point Point;
            /// <summary>If the message is WM_MOUSEWHEEL, the high-order word of this member is the wheel delta. The low-order word is reserved. A positive value indicates that the wheel was rotated forward, 
            /// away from the user; a negative value indicates that the wheel was rotated backward, toward the user. One wheel click is defined as WHEEL_DELTA, which is 120. 
            ///If the message is WM_XBUTTONDOWN, WM_XBUTTONUP, WM_XBUTTONDBLCLK, WM_NCXBUTTONDOWN, WM_NCXBUTTONUP, or WM_NCXBUTTONDBLCLK, the high-order word specifies which X button was pressed or released, 
            /// and the low-order word is reserved. This value can be one or more of the following values. Otherwise, MouseData is not used. 
            ///XBUTTON1
            ///The first X button was pressed or released.
            ///XBUTTON2
            ///The second X button was pressed or released.
            /// </summary>
            public int MouseData;
            /// <summary>Specifies the event-injected flag. An application can use the following value to test the mouse Flags. Value Purpose LLMHF_INJECTED Test the event-injected flag.  
            ///0
            ///Specifies whether the event was injected. The value is 1 if the event was injected; otherwise, it is 0.
            ///1-15
            ///Reserved.
            /// </summary>
            public int Flags;
            /// <summary>Specifies the Time stamp for this message.</summary>
            public int Time;
            /// <summary>Specifies extra information associated with the message.</summary>
            public int ExtraInfo;
        }
        #endregion
    }
}