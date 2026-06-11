// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.IO;
using LibreLancer;
using LibreLancer.Interface;

namespace InterfaceEdit;

internal class MainClass
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--compile")
        {
            CompileBundle(args[1], args[2]);
            return;
        }
        MainWindow? mw = null;
        AppHandler.Run(() =>
        {
            mw = new MainWindow();
            mw.Run();
        }, () => mw?.Crashed());
    }

    // Headless build of the embedded UI bundle:
    //   InterfaceEdit --compile <xmlFolder> <interface.json>
    private static void CompileBundle(string xmlFolder, string outputFile)
    {
        var resources = InterfaceResources.FromXml(
            File.ReadAllText(Path.Combine(xmlFolder, "resources.xml")));
        var bundle = Compiler.Compile(xmlFolder, new UiXmlLoader(resources));
        File.WriteAllText(outputFile, bundle.ToJSON());
        Console.WriteLine($"Compiled {xmlFolder} -> {outputFile}");
    }
}
