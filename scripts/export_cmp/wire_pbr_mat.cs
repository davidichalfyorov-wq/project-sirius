using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibreLancer;
using LibreLancer.Data;
using LibreLancer.Utf;
using LibreLancer.Utf.Cmp;
using LibreLancer.Utf.Vms;
using LibreLancer.ContentEdit;

// Add PBR scalars (m_factor / r_factor) to the materials used by a ship cmp, so the
// engine's BasicMaterial.Use() sees Metallic/Roughness != null -> renders via the PBR
// shader (+ IBL when an environment probe exists). Optionally also wires nm/mt/rt names.
// Usage: wire_pbr_mat <ship.cmp> <in.mat> <out.mat> [metallic] [roughness] [nm] [mt] [rt]
class WirePbr
{
    static int Main(string[] a)
    {
        if (a.Length < 3) { Console.Error.WriteLine("usage: wire_pbr_mat ship.cmp in.mat out.mat [metallic] [roughness] [nm mt rt]"); return 2; }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        float metallic = a.Length > 3 ? float.Parse(a[3], inv) : 0.15f;
        float roughness = a.Length > 4 ? float.Parse(a[4], inv) : 0.40f;
        string nm = a.Length > 5 ? a[5] : null;
        string mt = a.Length > 6 ? a[6] : null;
        string rt = a.Length > 7 ? a[7] : null;

        CmpFile cmp;
        using (var fs = File.OpenRead(a[0])) cmp = new CmpFile(a[0], fs);

        var crcs = new HashSet<uint>();
        if (cmp.VMeshLibrary != null)
            foreach (var vms in cmp.VMeshLibrary.Meshes.Values)
                foreach (var mh in vms.Meshes)
                    crcs.Add(mh.MaterialCrc);
        Console.WriteLine($"cmp references {crcs.Count} material CRCs");

        var utf = new EditableUtf(a[1]);
        int edited = 0;
        foreach (var lib in utf.Root.Children)
        {
            if (!lib.Name.Equals("material library", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var mat in lib.Children)
            {
                var crc = CrcTool.FLModelCrc(mat.Name);
                if (!crcs.Contains(crc)) continue;
                void SetFloat(string nm2, float v)
                {
                    mat.Children.RemoveAll(c => c.Name.Equals(nm2, StringComparison.OrdinalIgnoreCase));
                    mat.Children.Add(LUtfNode.FloatNode(mat, nm2, v));
                }
                void SetStr(string nm2, string v)
                {
                    if (string.IsNullOrEmpty(v)) return;
                    mat.Children.RemoveAll(c => c.Name.Equals(nm2, StringComparison.OrdinalIgnoreCase));
                    mat.Children.Add(LUtfNode.StringNode(mat, nm2, v));
                }
                SetFloat("m_factor", metallic);
                SetFloat("r_factor", roughness);
                SetStr("nm_name", nm);
                SetStr("mt_name", mt);
                SetStr("rt_name", rt);
                edited++;
                Console.WriteLine($"  PBR-wired: {mat.Name}  (crc 0x{crc:X8})");
            }
        }
        utf.Save(a[2], 0);
        Console.WriteLine($"Done. Wired {edited} materials -> {a[2]}");
        return edited > 0 ? 0 : 1;
    }
}
