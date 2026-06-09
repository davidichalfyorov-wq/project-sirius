// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

namespace LibreLancer.Data.Dll;

public class ResourceDll
{
    [StructLayout(LayoutKind.Sequential)]
    private struct IMAGE_RESOURCE_DIRECTORY //Size: 16
    {
        public uint Characteristics;
        public uint TimeDateStamp;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public ushort NumberOfNamedEntries;
        public ushort NumberOfIdEntries;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] //Size: 8
    private struct IMAGE_RESOURCE_DIRECTORY_ENTRY
    {
        public uint Name;
        public uint OffsetToData; //Relative to rsrc
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IMAGE_RESOURCE_DATA_ENTRY
    {
        public uint OffsetToData; //Relative to start of dll
        public uint Size;
        public uint CodePage;
        public uint Reserved;
    }

    private class ResourceTable
    {
        public uint Type;
        public List<Resource> Resources = [];
    }

    private class Resource
    {
        public uint Name;
        public List<ResourceData> Locales = [];
    }

    private class ResourceData
    {
        public uint Locale;
        public ArraySegment<byte> Data;
    }

    private const uint RT_RCDATA = 23;
    private const uint RT_VERSION = 16;
    private const uint RT_STRING = 6;
    private const uint RT_MENU = 4;
    private const uint RT_DIALOG = 5;
    private const uint IMAGE_RESOURCE_NAME_IS_STRING = 0x80000000;
    private const uint IMAGE_RESOURCE_DATA_IS_DIRECTORY = 0x80000000;

    public Dictionary<int, string> Strings = new();
    public Dictionary<int, string> Infocards = new();
    public List<BinaryResource> Dialogs = [];
    public List<BinaryResource> Menus = [];
    public VersionInfoResource VersionInfo = null!;

    public string? SavePath;

    public static ResourceDll FromFile(string path)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        using var file = File.OpenRead(path);

        return FromStream(file, path);

    }

    public static ResourceDll FromStream(Stream stream, string? savePath = null)
    {
        var dll = new ResourceDll {SavePath = savePath};
        var (rSrcOffset, rSrc) = ReadPE(stream);

        var directory = Struct<IMAGE_RESOURCE_DIRECTORY>(rSrc, 0);

        List<ResourceTable> resources = [];
        for (var i = 0; i < directory.NumberOfNamedEntries + directory.NumberOfIdEntries; i++)
        {
            var off = 16 + (i * 8);
            var entry = Struct <IMAGE_RESOURCE_DIRECTORY_ENTRY>(rSrc, off);
            if ((IMAGE_RESOURCE_NAME_IS_STRING & entry.Name) == IMAGE_RESOURCE_NAME_IS_STRING)
            {
                continue;
            }

            resources.Add(ReadResourceTable(rSrcOffset, DirOffset(entry.OffsetToData), rSrc, entry.Name));
        }

        var name = string.IsNullOrWhiteSpace(savePath) ? "dll" : Path.GetFileName(savePath);

        foreach (var table in resources)
        {
            switch (table.Type)
            {
                case RT_RCDATA:
                {
                    foreach (var res in table.Resources)
                    {
                        if (!TrySelectLocale(res, out var locale))
                        {
                            continue;
                        }

                        var idx = locale.Data.Offset;
                        var count = locale.Data.Count;
                        if (locale.Data.Count > 2)
                        {
                            if (locale.Data.Count % 2 == 1 && locale.Data[^1] == 0)
                            {
                                //skip extra NULL byte
                                count--;
                            }
                            if (locale.Data[0] == 0xFF && locale.Data[1] == 0xFE)
                            {
                                //skip BOM
                                idx += 2;
                                count -= 2;
                            }
                        }

                        try
                        {
                            dll.Infocards[(int)res.Name] = Encoding.Unicode.GetString(locale.Data.Array!, idx, count);
                        }
                        catch (Exception)
                        {
                            FLLog.Error("Infocards", $"{name}: Infocard Corrupt: {res.Name}");
                        }
                    }

                    break;
                }
                case RT_STRING:
                {
                    foreach (var res in table.Resources)
                    {
                        if (!TrySelectLocale(res, out var locale))
                        {
                            continue;
                        }

                        var blockId = (int)((res.Name - 1u) * 16);
                        var seg = locale.Data;
                        using var reader = new BinaryReader(new MemoryStream(seg.Array!, seg.Offset, seg.Count));

                        for (var j = 0; j < 16; j++)
                        {
                            if (reader.BaseStream.Position + 2 > reader.BaseStream.Length)
                            {
                                break;
                            }

                            var length = reader.ReadUInt16() * 2;

                            if (length == 0)
                            {
                                continue;
                            }

                            if (reader.BaseStream.Position + length > reader.BaseStream.Length)
                            {
                                FLLog.Error("Strings", $"{name}: String block corrupt: {res.Name}");
                                break;
                            }

                            var bytes = reader.ReadBytes(length);
                            var str = Encoding.Unicode.GetString(bytes);
                            dll.Strings[blockId + j] = str;
                        }
                    }

                    break;
                }
                case RT_VERSION when table.Resources.Count > 0:
                    if (TrySelectLocale(table.Resources[0], out var versionLocale))
                    {
                        dll.VersionInfo = new VersionInfoResource(versionLocale.Data.ToArray());
                    }
                    break;
                case RT_DIALOG:
                {
                    foreach (var dlg in table.Resources)
                    {
                        if (TrySelectLocale(dlg, out var dialogLocale))
                        {
                            dll.Dialogs.Add(new BinaryResource(dlg.Name, dialogLocale.Data.ToArray()));
                        }
                    }

                    break;
                }
                case RT_MENU:
                {
                    foreach (var dlg in table.Resources)
                    {
                        if (TrySelectLocale(dlg, out var menuLocale))
                        {
                            dll.Menus.Add(new BinaryResource(dlg.Name, menuLocale.Data.ToArray()));
                        }
                    }

                    break;
                }
            }
        }

        return dll;
    }

    private static bool TrySelectLocale(Resource res, out ResourceData locale)
    {
        if (res.Locales.Count == 0)
        {
            locale = null!;
            return false;
        }

        // Prefer English/neutral resources, but accept the first locale for mods that only
        // ship one resource language. This mirrors Windows' resource lookup well enough for
        // Freelancer DLLs and avoids losing Discovery strings on Linux.
        locale = res.Locales.FirstOrDefault(x => x.Locale is 0 or 0x409) ?? res.Locales[0];
        return true;
    }

    private static int DirOffset(uint a) => (int)(a & 0x7FFFFFFF);

    private static ResourceTable ReadResourceTable(int rsrcOffset, int offset, byte[] rsrc, uint type)
    {
        var directory = Struct<IMAGE_RESOURCE_DIRECTORY>(rsrc, offset);
        var table = new ResourceTable() {Type = type};
        for (var i = 0; i < directory.NumberOfNamedEntries + directory.NumberOfIdEntries; i++)
        {
            var off = offset + 16 + (i * 8);
            var entry = Struct<IMAGE_RESOURCE_DIRECTORY_ENTRY>(rsrc, off);
            var res = new Resource() { Name = entry.Name };
            if ((IMAGE_RESOURCE_DATA_IS_DIRECTORY & entry.OffsetToData) != IMAGE_RESOURCE_DATA_IS_DIRECTORY)
            {
                throw new Exception("Malformed .rsrc section");
            }

            var langDirectory = Struct<IMAGE_RESOURCE_DIRECTORY>(rsrc, DirOffset(entry.OffsetToData));
            for (var j = 0; j < langDirectory.NumberOfIdEntries + langDirectory.NumberOfNamedEntries; j++)
            {
                var langOff = DirOffset(entry.OffsetToData) + 16 + (j * 8);
                var langEntry = Struct<IMAGE_RESOURCE_DIRECTORY_ENTRY>(rsrc, langOff);
                if((IMAGE_RESOURCE_DATA_IS_DIRECTORY & langEntry.OffsetToData) == IMAGE_RESOURCE_DATA_IS_DIRECTORY)
                {
                    throw new Exception("Malformed .rsrc section");
                }

                var dataEntry = Struct<IMAGE_RESOURCE_DATA_ENTRY>(rsrc, (int)langEntry.OffsetToData);
                var dataOffset = (int)dataEntry.OffsetToData - rsrcOffset;
                var dataSize = (int)dataEntry.Size;
                if (dataOffset < 0 || dataSize < 0 || dataOffset + dataSize > rsrc.Length)
                {
                    FLLog.Warning("Dll", $"Skipping malformed resource {type}:{res.Name}:{langEntry.Name}");
                    continue;
                }
                var dat = new ArraySegment<byte>(rsrc, dataOffset, dataSize);
                res.Locales.Add(new ResourceData() {Locale = langEntry.Name, Data = dat});
            }

            table.Resources.Add(res);
        }

        return table;
    }

    private static T Struct<T>(byte[] bytes, int offset) where T : unmanaged
    {
        var sz = Marshal.SizeOf(typeof(T));
        if (offset < 0 || offset + sz > bytes.Length)
        {
            throw new IndexOutOfRangeException();
        }

        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject() + offset, typeof(T))!;
        }
        finally {
            handle.Free();
        }
    }

    private static (int, byte[]) ReadPE(Stream stream)
    {
        using var pe = new PEReader(stream);

        var fullImage = pe.GetEntireImage().GetContent();
        var offset = 0;
        var rawStart = 0;
        var foundResources = false;
        foreach (var h in pe.PEHeaders.SectionHeaders.Where(h => h.Name == ".rsrc"))
        {
            offset = h.VirtualAddress;
            rawStart = h.PointerToRawData;
            foundResources = true;
        }

        if (!foundResources)
        {
            throw new InvalidDataException("PE file does not contain a .rsrc section");
        }

        //allow reading past end of .rsrc section
        var array = new byte[fullImage.Length - rawStart];
        fullImage.CopyTo(rawStart, array, 0, array.Length);
        return (offset, array);

    }
}
