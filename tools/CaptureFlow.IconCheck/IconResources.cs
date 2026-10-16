using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class CaptureFlowIconResources {
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
 [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
 [DllImport("kernel32.dll", EntryPoint="FindResourceW", SetLastError=true)] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
 [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
 [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
 [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
 delegate bool Callback(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
 [DllImport("kernel32.dll", EntryPoint="EnumResourceNamesW", SetLastError=true)] static extern bool EnumResourceNames(IntPtr module, IntPtr type, Callback callback, IntPtr parameter);
 static byte[] Read(IntPtr module, IntPtr name, int type) {
   var resource=FindResource(module,name,new IntPtr(type)); if(resource==IntPtr.Zero) throw new InvalidOperationException("Missing native resource: "+type);
   var bytes=new byte[SizeofResource(module,resource)]; Marshal.Copy(LockResource(LoadResource(module,resource)),bytes,0,bytes.Length); return bytes;
 }
 public static byte[][] Frames(string path) {
   // Resource-only mapping: never execute the application's entry point or load its dependencies.
   var module=LoadLibraryEx(path,IntPtr.Zero,0x22); if(module==IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   try {
     var frames=new List<byte[]>(); int groups=0;
     Callback callback=(m,t,n,p)=> { groups++; var group=Read(m,n,14); int count=BitConverter.ToUInt16(group,4);
       for(int i=0;i<count;i++) frames.Add(Read(m,new IntPtr(BitConverter.ToUInt16(group,6+i*14+12)),3)); return true; };
     if(!EnumResourceNames(module,new IntPtr(14),callback,IntPtr.Zero)||groups!=1) throw new InvalidOperationException("Expected exactly one application icon group, found "+groups);
     return frames.ToArray();
   } finally { FreeLibrary(module); }
 }
}
