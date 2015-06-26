/// Managed-OSVR binding
///
/// <copyright>
/// Copyright 2014, 2015 Sensics, Inc. and contributors
///
/// Licensed under the Apache License, Version 2.0 (the "License");
/// you may not use this file except in compliance with the License.
/// You may obtain a copy of the License at
///
///     http://www.apache.org/licenses/LICENSE-2.0
///
/// Unless required by applicable law or agreed to in writing, software
/// distributed under the License is distributed on an "AS IS" BASIS,
/// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
/// See the License for the specific language governing permissions and
/// limitations under the License.
/// </copyright>

﻿using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Runtime.ConstrainedExecution;

#if !MANAGED_OSVR_INTERNAL_PINVOKE

using System.IO;

#endif

namespace OSVR
{
    namespace ClientKit
    {
        public sealed class SafeClientContextHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SafeClientContextHandle() : base(true) { }

            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
            protected override bool ReleaseHandle()
            {
                System.Diagnostics.Debug.WriteLine("[OSVR] ClientContext shutdown");
                return ClientContext.osvrClientShutdown(handle) == OSVR.ClientKit.ClientContext.OSVR_RETURN_SUCCESS;
            }
        }

        /// @brief Client context object: Create and keep one in your application.
        /// Handles lifetime management and provides access to ClientKit
        /// functionality.
        /// @ingroup ClientKitCPP
        public class ClientContext : IDisposable
        {
            #region ClientKit C functions

            // Should be defined if used with Unity and UNITY_IOS or UNITY_XBOX360 are defined
#if MANAGED_OSVR_INTERNAL_PINVOKE
            // On iOS and Xbox 360, plugins are statically linked into
            // the executable, so we have to use __Internal as the
            // library name.
            private const string OSVRCoreDll = "__Internal";
#else
            private const string OSVRCoreDll = "osvrClientKit";
#endif

            public static Byte OSVR_RETURN_SUCCESS = 0x0;
            public static Byte OSVR_RETURN_FAILURE = 0x1;

            [DllImport(OSVRCoreDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static SafeClientContextHandle osvrClientInit([MarshalAs(UnmanagedType.LPStr)] string applicationIdentifier, [MarshalAs(UnmanagedType.U4)] uint flags);

            [DllImport(OSVRCoreDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static Byte osvrClientUpdate(SafeClientContextHandle ctx);

            [DllImport(OSVRCoreDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static Byte osvrClientShutdown(IntPtr /*OSVR_ClientContext*/ ctx);

            [DllImport(OSVRCoreDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static Byte osvrClientGetStringParameterLength(SafeClientContextHandle ctx, string path, out UIntPtr len);

            [DllImport(OSVRCoreDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static Byte osvrClientGetStringParameter(SafeClientContextHandle ctx, string path, StringBuilder buf, UIntPtr len);

            #endregion ClientKit C functions

            #region Support for locating native libraries

            /// <summary>
            /// Try finding the right path for the p/invoked DLLs before p/invoke tries to.
            /// </summary>
            static public void PreloadNativeLibraries()
            {
#if !MANAGED_OSVR_INTERNAL_PINVOKE
                String path;
                // This line based on http://stackoverflow.com/a/864497/265522
                var assembly = System.Uri.UnescapeDataString((new System.Uri(System.Reflection.Assembly.GetExecutingAssembly().CodeBase)).AbsolutePath);
                var assemblyPath = Path.GetDirectoryName(assembly);
                System.Diagnostics.Debug.WriteLine("[OSVR] ClientKit assembly directory: " + assemblyPath);
                if (IntPtr.Size == 8)
                {
                    path = new PathChecker(assemblyPath).Check("x86_64").Check("x64").Check("64").Result;
                }
                else
                {
                    path = new PathChecker(assemblyPath).Check("x86").Check("32").Result;
                }
                if (path.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine("[OSVR] Found ClientKit native libraries in directory: " + path);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[OSVR] Could not preload ClientKit native libraries");
                }
#endif
            }

#if !MANAGED_OSVR_INTERNAL_PINVOKE

            private class PathChecker
            {
                [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
                private static extern IntPtr LoadLibrary([MarshalAs(UnmanagedType.LPStr)]string lpFileName);

                public PathChecker(string path)
                {
                    AssemblyPath = path;
                    CheckDir("");
                    CheckDir(path);
                }

                public PathChecker Check(string suffix)
                {
                    if (!KeepTrying)
                    {
                        return this;
                    }
                    CheckDir(Path.Combine(AssemblyPath, suffix));
                    return this;
                }

                public string Result
                {
                    get
                    {
                        return DllPath;
                    }
                }

                private void CheckDir(string dir)
                {
                    if (!KeepTrying)
                    {
                        return;
                    }
                    if (CheckDir(dir, "", ".dll") || CheckDir(dir, "lib", ".so"))
                    {
                        DllPath = dir;
                        KeepTrying = false;
                    }
                }

                private bool CheckDir(string dir, string prefix, string suffix)
                {
                    if (!KeepTrying)
                    {
                        return false;
                    }
                    bool success = true;
                    // @todo cross-platform this: can we change the name
                    // of the pinvoked native module or something? Right
                    // now we just catch and ignore the p/invoke
                    // exception here.
                    try
                    {
                        foreach (var lib in NativeLibs)
                        {
                            var libName = produceLibPath(dir, prefix, suffix, lib);
                            System.Diagnostics.Debug.WriteLine(String.Format("[OSVR] Trying to LoadLibrary: {0}", libName));
                            var result = LoadLibrary(libName);
                            if (result == IntPtr.Zero)
                            {
                                // Later ones depend on earlier ones, so if one fails, this was not the right approach.
                                success = false;
                                break;
                            }
                        }
                    }
                    catch (DllNotFoundException e)
                    {
                        System.Diagnostics.Debug.WriteLine(String.Format("[OSVR] Caught exception trying to LoadLibrary: {0}", e));
                        success = false;
                        KeepTrying = false;
                    }
                    return success;
                }

                private String produceLibPath(string dir, string prefix, string suffix, string lib)
                {
                    return dir.Length > 0 ? Path.Combine(dir, prefix + lib + suffix) : prefix + lib;
                }

                /// <summary>
                /// Native library names (without lib, .dll, or .so), in
                /// order of increasing dependency - that is, each
                /// library may only depend on libraries earlier in the list.
                /// </summary>
                private String[] NativeLibs = { "osvrUtil", "osvrCommon", "osvrClient", "osvrClientKit" };
                private string AssemblyPath;
                private string DllPath;
                private bool KeepTrying = true;
            }

#endif

            #endregion Support for locating native libraries

            /// @brief Initialize the library.
            /// @param applicationIdentifier A string identifying your application.
            /// Reverse DNS format strongly suggested.
            /// @param flags initialization options (reserved) - pass 0 for now.
            public ClientContext(string applicationIdentifier, uint flags)
            {
                this.m_context = osvrClientInit(applicationIdentifier, flags);
            }

            public ClientContext(string applicationIdentifier)
            {
                // Since this is likely the first p/invoke call, may throw
                // System.BadImageFormatException if our DLL-path magic didn't
                // put the right bitness of DLL in the path
                this.m_context = osvrClientInit(applicationIdentifier, 0);
            }

            ~ClientContext()
            {
                Dispose(false);
            }

            /// @brief Destructor: Shutdown the library.
            public void Dispose()
            {
                System.Diagnostics.Debug.WriteLine("[OSVR] In ClientContext.Dispose()");
                Dispose(true);
                // No need to call the finalizer since we've now cleaned
                // up the unmanaged memory.
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                System.Diagnostics.Debug.WriteLine(String.Format("[OSVR] In ClientContext.Dispose({0})", disposing));
                if (disposing)
                {
                    if (this.m_context != null && !this.m_context.IsInvalid)
                    {
                        this.m_context.Dispose();
                        this.m_context = null;
                    }
                }
            }

            /// @brief Updates the state of the context - call regularly in your
            /// mainloop.
            public void update()
            {
                Byte ret = osvrClientUpdate(this.m_context);
                if (OSVR_RETURN_SUCCESS != ret)
                {
                    throw new ApplicationException("Error updating context.");
                }
            }

            /// @brief Get the interface associated with the given path.
            /// @param path A resource path.
            /// @returns The interface object.
            public Interface getInterface(string path)
            {
                SafeClientInterfaceHandle iface = new SafeClientInterfaceHandle();
                Byte ret = Interface.osvrClientGetInterface(this.m_context, path, ref iface);
                if (OSVR_RETURN_SUCCESS != ret)
                {
                    throw new ArgumentException("Couldn't create interface because the path was invalid.");
                }

                return new Interface(iface);
            }

            /// @brief Get a string parameter value from the given path.
            /// @param path A resource path.
            /// @returns parameter value, or empty string if parameter does not
            /// exist or is not a string.
            public string getStringParameter(string path)
            {
                UIntPtr length = UIntPtr.Zero;
                Byte ret = osvrClientGetStringParameterLength(m_context, path, out length);
                if (OSVR_RETURN_SUCCESS != ret)
                {
                    throw new ArgumentException("Invalid context or null reference to length variable.");
                }

                if (UIntPtr.Zero == length)
                {
                    return "";
                }

                StringBuilder buf = new StringBuilder((int)(length.ToUInt32()));
                ret = osvrClientGetStringParameter(m_context, path, buf, length);
                if (OSVR_RETURN_SUCCESS != ret)
                {
                    throw new ApplicationException("Invalid context, null reference to buffer, or buffer is too small.");
                }

                return buf.ToString();
            }

            private SafeClientContextHandle m_context;
        }
    } // end namespace ClientKit
} // end namespace OSVR
