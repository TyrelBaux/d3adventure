using System;
using Fasm;
using System.Diagnostics;
using D3_Adventures.Injector;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.ComponentModel;

namespace D3_Adventures.Memory_Handling
{
    public class MemoryManager : IDisposable
    {
        // Fields
        private int m_lpBytesRead;

        // Methods
        public MemoryManager(int hwnd)
        {
            this.ProcessHandle = Imports.OpenProcess(Imports.ProcessAccessFlags.All, false, hwnd);
            this.Asm = new ManagedFasm(this.ProcessHandle.DangerousGetHandle());
            this.D3Process = Process.GetProcessById(hwnd);
            this.ThreadHandle = Imports.OpenThread(0x1f03ff, false, (uint)this.D3Process.Threads[0].Id);
            if (this.ProcessHandle.IsInvalid)
            {
                Console.WriteLine(string.Concat(new object[] { "Failed To Open Process: ", hwnd, ". Error: ", Imports.GetLastError() }), "Error");
            }
        }

        public MemoryManager(IntPtr hwnd)
        {
            uint num;
            Imports.GetWindowThreadProcessId(hwnd, out num);
            this.ProcessHandle = Imports.OpenProcess(Imports.ProcessAccessFlags.All, false, (int)num);
            this.Asm = new ManagedFasm(this.ProcessHandle.DangerousGetHandle());
            this.D3Process = Process.GetProcessById((int)num);
            this.ThreadHandle = Imports.OpenThread(0x1f03ff, false, (uint)this.D3Process.Threads[0].Id);
            if (this.ProcessHandle.IsInvalid)
            {
                Console.WriteLine(string.Concat(new object[] { "Failed To Open Process: ", hwnd, ". Error: ", Imports.GetLastError() }), "Error");
            }
        }

        internal IntPtr AllocateMemory(int size, Imports.AllocationType type, Imports.MemoryProtection protect)
        {
            return Imports.VirtualAllocEx(this.ProcessHandle, 0, size, type, protect);
        }

        public void Attach()
        {
            wardenCheck();
            ProcessModule d3d = GetProcessModule("d3d9.dll");

            this.Injector = new ASMExecutor(this, new IntPtr(d3d.BaseAddress.ToInt32() + 0x2279f));
        }

        public void CreateRemoteThread(uint address, IntPtr parameter)
        {
            Imports.CreateRemoteThread(this.ProcessHandle.DangerousGetHandle(), IntPtr.Zero, 0, address, parameter, 0, IntPtr.Zero);
        }

        public void Dispose()
        {
            if (this.Injector != null)
            {
                this.Injector.Dispose();
                this.Injector = null;
            }
            if (this.Asm != null)
            {
                this.Asm.Dispose();
                this.Asm = null;
            }
            if (this.ThreadHandle != IntPtr.Zero)
            {
                this.ThreadHandle = IntPtr.Zero;
            }
            if (this.ProcessHandle != null)
            {

                this.ProcessHandle.Dispose();
                this.ProcessHandle = null;
            }
     
        }

        public static uint FindPattern(byte[] bData, byte[] bPattern, string szMask)
        {
            if ((bData == null) || (bData.Length == 0))
            {
                throw new ArgumentNullException("bData");
            }
            if ((bPattern == null) || (bPattern.Length == 0))
            {
                throw new ArgumentNullException("bPattern");
            }
            if (szMask == string.Empty)
            {
                throw new ArgumentNullException("szMask");
            }
            if (bPattern.Length != szMask.Length)
            {
                throw new ArgumentException("Pattern and Mask lengths must be the same.");
            }
            bool flag = false;
            int length = bPattern.Length;
            int num4 = bData.Length - length;
            for (int i = 0; i < num4; i++)
            {
                flag = true;
                for (int j = 0; j < length; j++)
                {
                    if (((szMask[j] == 'x') && (bPattern[j] != bData[i + j])) || ((szMask[j] == '!') && (bPattern[j] == bData[i + j])))
                    {
                        flag = false;
                        break;
                    }
                }
                if (flag)
                {
                    return (uint)i;
                }
            }
            return 0;
        }

        public uint FindPattern(uint dwStart, uint dwEnd, byte[] bPattern, string szMask)
        {
            byte[] buffer;
            if (dwStart > dwEnd)
            {
                throw new ArgumentException("Start Address cannot be bigger than the End Address");
            }
            int bufferLength = (int)(dwEnd - dwStart);
            if ((bPattern == null) || (bPattern.Length == 0))
            {
                throw new ArgumentNullException("bData");
            }
            if (bPattern.Length != szMask.Length)
            {
                throw new ArgumentException("bData and szMask must be of the same size");
            }
            this.ReadMemory(dwStart, bufferLength, out buffer);
            if (buffer == null)
            {
                throw new Exception("Could not read memory in FindPattern.");
            }
            uint num2 = FindPattern(buffer, bPattern, szMask);
            if (num2 == 0)
            {
                return 0;
            }
            return (dwStart + num2);
        }

        public uint FindPatternRetry(uint dwStart, uint dwEnd, byte[] bPattern, string szMask)
        {
            byte[] buffer;
            if (dwStart > dwEnd)
            {
                throw new ArgumentException("Start Address cannot be bigger than the End Address");
            }
            int bufferLength = (int)(dwEnd - dwStart);
            if ((bPattern == null) || (bPattern.Length == 0))
            {
                throw new ArgumentNullException("bData");
            }
            if (bPattern.Length != szMask.Length)
            {
                throw new ArgumentException("bData and szMask must be of the same size");
            }
            this.ReadMemoryRetry(dwStart, bufferLength, out buffer);
            if (buffer == null)
            {
                throw new Exception("Could not read memory in FindPattern.");
            }
            uint num2 = FindPattern(buffer, bPattern, szMask);
            if (num2 == 0)
            {
                return 0;
            }
            return (dwStart + num2);
        }

        internal bool FreeMemory(IntPtr adress)
        {
            return this.FreeMemory(adress, 0, Imports.MemoryFree.MemRelease);
        }

        internal bool FreeMemory(IntPtr adress, int size, Imports.MemoryFree type)
        {
            if (type == Imports.MemoryFree.MemRelease)
            {
                size = 0;
            }
            return Imports.VirtualFreeEx(this.ProcessHandle, adress, size, type);
        }

        public ProcessModule GetProcessModule(string name)
        {
            return this.D3Process.Modules.Cast<ProcessModule>().FirstOrDefault<ProcessModule>(p => (p.ModuleName == name));
        }

        public static object RawDeserialize(byte[] rawData, int position, Type anyType)
        {
            int cb = Marshal.SizeOf(anyType);
            if (cb > rawData.Length)
            {
                return null;
            }
            IntPtr destination = Marshal.AllocHGlobal(cb);
            Marshal.Copy(rawData, position, destination, cb);
            object obj2 = Marshal.PtrToStructure(destination, anyType);
            Marshal.FreeHGlobal(destination);
            return obj2;
        }

        public static byte[] RawSerialize(object anything)
        {
            int cb = Marshal.SizeOf(anything);
            IntPtr ptr = Marshal.AllocHGlobal(cb);
            Marshal.StructureToPtr(anything, ptr, false);
            byte[] destination = new byte[cb];
            Marshal.Copy(ptr, destination, 0, cb);
            Marshal.FreeHGlobal(ptr);
            return destination;
        }

        public object ReadMemory(IntPtr address, Type type)
        {
            byte[] buffer;
            this.ReadMemory(address, Marshal.SizeOf(type), out buffer);
            return RawDeserialize(buffer, 0, type);
        }

        public object ReadMemory(uint address, Type type)
        {
            if (address > 0x7fffffff)
            {
                return null;
            }
            return this.ReadMemory((IntPtr)address, type);
        }

        public uint ReadMemory(uint pointer, uint[] offsets)
        {
            uint num = this.ReadMemoryAsUint(pointer);
            foreach (uint num2 in offsets)
            {
                num = this.ReadMemoryAsUint(num + num2);
            }
            return num;
        }

        public bool ReadMemory(int memoryLocation, int bufferLength, out byte[] lpBuffer)
        {
            lpBuffer = new byte[bufferLength];
            if (this.ProcessHandle.IsInvalid || this.ProcessHandle.IsClosed)
            {
                return false;
            }
            if (!Imports.ReadProcessMemory(this.ProcessHandle.DangerousGetHandle(), (IntPtr)memoryLocation, lpBuffer, bufferLength, out this.m_lpBytesRead))
            {
                Console.WriteLine("Failed to read from Address: {0:X}. Error: {1}", memoryLocation, Imports.GetLastError());
                return false;
            }
            return true;
        }

        public bool ReadMemory(IntPtr memoryLocation, int bufferLength, out byte[] lpBuffer)
        {
            lpBuffer = new byte[bufferLength];
            if (this.ProcessHandle.IsInvalid || this.ProcessHandle.IsClosed)
            {
                return false;
            }
            if (!Imports.ReadProcessMemory(this.ProcessHandle.DangerousGetHandle(), memoryLocation, lpBuffer, bufferLength, out this.m_lpBytesRead))
            {
                Console.WriteLine("Failed to read from Address: {0:X}. Error: {1}", memoryLocation, Imports.GetLastError());
                return false;
            }
            if (this.m_lpBytesRead != bufferLength)
            {
                Console.WriteLine("Failed to read the correct amount of bytes");
                return false;
            }
            return true;
        }

        public bool ReadMemory(uint memoryLocation, int bufferLength, out uint uintOut)
        {
            uintOut = 0;
            if (this.ProcessHandle.IsInvalid || this.ProcessHandle.IsClosed)
            {
                return false;
            }
            byte[] lpBuffer = new byte[4];
            if (!Imports.ReadProcessMemory(this.ProcessHandle.DangerousGetHandle(), (IntPtr)memoryLocation, lpBuffer, bufferLength, out this.m_lpBytesRead))
            {
                Console.WriteLine("Failed to read from Address: {0:X}. Error: {1}", memoryLocation, Imports.GetLastError());
                return false;
            }
            uintOut = BitConverter.ToUInt32(lpBuffer, 0);
            return true;
        }

        public bool ReadMemory(uint memoryLocation, int bufferLength, out byte[] lpBuffer)
        {
            lpBuffer = new byte[bufferLength];
            if (memoryLocation > 0x7fffffff)
            {
                return false;
            }
            if (this.ProcessHandle.IsInvalid || this.ProcessHandle.IsClosed)
            {
                return false;
            }
            if (!Imports.ReadProcessMemory(this.ProcessHandle.DangerousGetHandle(), (IntPtr)memoryLocation, lpBuffer, bufferLength, out this.m_lpBytesRead))
            {
                Console.WriteLine("Failed to read from Address: {0:X}. Error: {1}", memoryLocation, Imports.GetLastError());
                return false;
            }
            return true;
        }

        public float ReadMemoryAsFloat(uint location)
        {
            byte[] buffer;
            this.ReadMemory(location, 4, out buffer);
            return BitConverter.ToSingle(buffer, 0);
        }

        public string ReadMemoryAsHex(uint location, int length)
        {
            byte[] buffer;
            this.ReadMemory(location, length, out buffer);
            StringBuilder builder = new StringBuilder();
            string str = "";
            int num = 0;
            foreach (byte num2 in buffer)
            {
                str = num2.ToString("X");
                if (num2 < 0x10)
                {
                    builder.Append("0" + str + " ");
                }
                else
                {
                    builder.Append(str + " ");
                }
                num++;
            }
            return builder.ToString().Trim();
        }

        public int ReadMemoryAsInt(uint location)
        {
            byte[] buffer;
            this.ReadMemory(location, 4, out buffer);
            return BitConverter.ToInt32(buffer, 0);
        }

        public int ReadMemoryAsIntRetry(uint location)
        {
            byte[] buffer;
            this.ReadMemoryRetry(location, 4, out buffer);
            return BitConverter.ToInt32(buffer, 0);
        }

        public string ReadMemoryAsString(uint location, int length)
        {
            byte[] buffer;
            this.ReadMemory(location, length, out buffer);
            return Encoding.ASCII.GetString(buffer, 0, length).TrimEnd(new char[1]);
        }
        public string ReadMemoryAsString(uint location, int length, Encoding encoding)
        {
            byte[] buffer;
            this.ReadMemory(location, length, out buffer);
            return encoding.GetString(buffer, 0, length).TrimEnd(new char[1]);
        }
        public uint ReadMemoryAsUint(uint location)
        {
            byte[] buffer;
            this.ReadMemory(location, 4, out buffer);
            return BitConverter.ToUInt32(buffer, 0);
        }


        public unsafe T UnsafeReadToStruct<T>(IntPtr address) where T : struct
        {

            byte[] buffer = new byte[Marshal.SizeOf(typeof(T))];
            fixed (byte* numRef = buffer)
            {
                if (((buffer = unsafeRead(address, Marshal.SizeOf(typeof(T)))) != null) && (buffer.Length != 0))
                {
                    return UnsafeReadStructure<T>((IntPtr)numRef);
                }
                else
                    return new T();
            }

        }


        public bool ReadMemoryRetry(uint memoryLocation, int bufferLength, out byte[] lpBuffer)
        {
            bool flag = false;
            lpBuffer = new byte[bufferLength];
            if (memoryLocation > 0x7fffffff)
            {
                return false;
            }
            if (this.ProcessHandle.IsInvalid || this.ProcessHandle.IsClosed)
            {
                return false;
            }
            while (!flag)
            {
                try
                {
                    Imports.ReadProcessMemory(this.ProcessHandle.DangerousGetHandle(), (IntPtr)memoryLocation, lpBuffer, bufferLength, out this.m_lpBytesRead);
                    flag = true;
                }
                catch
                {
                }
            }
            return true;
        }

        public unsafe T UnsafeReadPointersToStruct<T>(bool isRelative = false, params IntPtr[] addresses) where T : struct
        {
            if (addresses.Length == 0)
            {
                throw new InvalidOperationException("Cannot read a value from unspecified addresses.");
            }
            if (addresses.Length == 1)
            {
                return this.UnsafeReadToStruct<T>(addresses[0]);
            }
            IntPtr ptr = this.UnsafeReadToStruct<IntPtr>(addresses[0]);
            for (int i = 1; i < (addresses.Length - 1); i++)
            {
                ptr = this.UnsafeReadToStruct<IntPtr>(ptr + ((int)addresses[i]));
            }
            return this.UnsafeReadToStruct<T>(ptr + ((int)addresses[addresses.Length - 1]));
        }

        public unsafe byte[] unsafeRead(IntPtr address, int count, bool isRelative = false)
        {
            byte[] buffer3;
            if (count == 0)
            {
                throw new ArgumentException("Cannot read 0 bytes", "count");
            }
            byte[] buffer = new byte[count];
            if (((buffer3 = buffer) != null) && (buffer3.Length != 0))
            {
                fixed (byte* numRef = buffer3)
                {
                    int num;
   
                    if (Imports.ReadProcessMemory(this.ProcessHandle, address, numRef, count, out num) && (num == count))
                    {
                        return buffer;
                    }
                }
                throw new AccessViolationException(string.Format("Could not read bytes from  {0:X}", address), new Win32Exception(Marshal.GetLastWin32Error()));
            }
            return null;
        }

        public T ReadMemory<T>(IntPtr address) where T : struct
        {
            byte[] buffer;
            this.ReadMemory(address, Marshal.SizeOf(typeof(T)), out buffer);
            return (T)RawDeserialize(buffer, 0, typeof(T));
        }

        public unsafe T[] ReadArray<T>(IntPtr address, int elements, bool isRelative = false) where T : struct
        {
            T[] result = new T[elements];
            int size = Marshal.SizeOf(typeof(T));
            for (int i = 0; i < elements; i++)
            {
                result[i] = ReadMemory<T>(address + i * size);
            }
            return result;
        }

        public IntPtr GetPtrFromVTable(IntPtr address, int index)
        {
            IntPtr ptr = this.UnsafeReadToStruct<IntPtr>(address);
            return this.UnsafeReadToStruct<IntPtr>(ptr + (index * 4));
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        private unsafe T UnsafeReadStructure<T>(IntPtr address) where T : struct
        {
            T local2;
            try
            {
                object obj2;
                if (address == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Cannot retrieve a value at address 0");
                }
                switch (StructureWrapperOld<T>.typeCode_0)
                {
                    case TypeCode.Object:
                        if (!(StructureWrapperOld<T>.type_0 == typeof(IntPtr)))
                        {
                            break;
                        }
                        return (T)Convert.ChangeType(address, typeof(T));

                    case TypeCode.Boolean:
                        obj2 = !(address == IntPtr.Zero);
                        goto Label_01BE;

                    case TypeCode.Char:
                        obj2 = (char)address;
                        goto Label_01BE;

                    case TypeCode.SByte:
                        obj2 = (sbyte)address;
                        goto Label_01BE;

                    case TypeCode.Byte:
                        obj2 = (byte)address;
                        goto Label_01BE;

                    case TypeCode.Int16:
                        obj2 = (short)address;
                        goto Label_01BE;

                    case TypeCode.UInt16:
                        obj2 = (ushort)address;
                        goto Label_01BE;

                    case TypeCode.Int32:
                        obj2 = (int)address;
                        goto Label_01BE;

                    case TypeCode.UInt32:
                        obj2 = (uint)address;
                        goto Label_01BE;

                    case TypeCode.Int64:
                        obj2 = (long)address;
                        goto Label_01BE;

                    case TypeCode.UInt64:
                        obj2 = (ulong)address;
                        goto Label_01BE;

                    case TypeCode.Single:
                        obj2 = (float)address;
                        goto Label_01BE;

                    case TypeCode.Double:
                        obj2 = (double)address;
                        goto Label_01BE;

                    case TypeCode.Decimal:
                        obj2 = (decimal)address;
                        goto Label_01BE;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
                if (!StructureWrapperOld<T>.bool_0)
                {
                    T local = default(T);
                    Imports.RtlMoveMemory(StructureWrapperOld<T>.delegate1_0(ref local), (void*)address, StructureWrapperOld<T>.int_0);
                    return local;
                }
                obj2 = Marshal.PtrToStructure(address, typeof(T));
            Label_01BE:
                local2 = (T)obj2;
            }
            catch (AccessViolationException)
            {
                Trace.WriteLine(string.Concat(new object[] { "Access Violation on ", address, " with type ", typeof(T).Name }));
                local2 = default(T);
            }
            return local2;
        }

        private static void wardenCheck()
        {
            if (!Globals.screwWarden)
            {
                throw new Exception("This action may be dangerous and cause warden to detect and ban you, if you want to use this function set screwWarden to true in Data.cs");
            }
        }

        public bool WriteMemory(IntPtr memoryLocation, int bufferLength, ref byte[] lpBuffer)
        {
            int num;
            wardenCheck();
            if (this.ProcessHandle.IsInvalid || this.ProcessHandle.IsClosed)
            {
                return false;
            }
            Imports.WriteProcessMemory(this.ProcessHandle.DangerousGetHandle(), memoryLocation, lpBuffer, bufferLength, out num);
            if (num != bufferLength)
            {
                return false;
            }
            return true;
        }

        public bool WriteMemory(uint memoryLocation, int bufferLength, ref byte[] lpBuffer)
        {
            return this.WriteMemory((IntPtr)memoryLocation, bufferLength, ref lpBuffer);
        }

        public bool WriteMemoryAsFloat(uint location, float _float)
        {
            byte[] bytes = BitConverter.GetBytes(_float);
            return this.WriteMemory(location, bytes.Length, ref bytes);
        }

        public bool WriteMemoryAsInt(uint location, int integer)
        {
            byte[] bytes = BitConverter.GetBytes(integer);
            return this.WriteMemory(location, bytes.Length, ref bytes);
        }

        public bool WriteText(IntPtr address, string value, Encoding encoding)
        {
            if (value[value.Length - 1] != '\0')
            {
                value = value + '\0';
            }
            byte[] bytes = encoding.GetBytes(value);
            return this.WriteMemory(address, bytes.Length, ref bytes);
        }

        public bool WriteStructure<T>(IntPtr address, T value) where T : struct
        {
            int num;
            uint num2;
            int Size = Marshal.SizeOf(value);
            IntPtr ptr = Marshal.AllocHGlobal(Size);
            Marshal.StructureToPtr(value, ptr, false);
            byte[] destination = new byte[Size];
            Marshal.Copy(ptr, destination, 0, Size);
            Marshal.FreeHGlobal(ptr);
            Imports.VirtualProtectEx(ProcessHandle, address, (IntPtr)Size, 0x40, out num2);
            bool flag = Imports.WriteProcessMemory(ProcessHandle.DangerousGetHandle(), address, destination, Size, out num);
            Imports.VirtualProtectEx(ProcessHandle, address, (IntPtr)Size, num2, out num2);
            return flag;
        }

        // Properties
        public ManagedFasm Asm { get; set; }

        private Process D3Process { get; set; }

        public ASMExecutor Injector { get; set; }

        public bool IsProcessOpen
        {
            get
            {
                return (((this.ProcessHandle != null) && !this.ProcessHandle.IsClosed) && !this.ProcessHandle.IsInvalid);
            }
        }

        public bool IsThreadOpen
        {
            get
            {
                return (this.ThreadHandle != IntPtr.Zero);
            }
        }

        private SafeProcessHandle ProcessHandle { get; set; }

        private IntPtr ThreadHandle { get; set; }
    }

}