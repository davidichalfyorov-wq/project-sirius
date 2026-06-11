using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using LibreLancer.Data;
using LibreLancer.Data.GameData;
using LibreLancer.Data.GameData.World;
using LibreLancer.Data.IO;
using LibreLancer.Graphics;
using LibreLancer.Items;
using LibreLancer.Physics;
using LibreLancer.Resources;
using LibreLancer.Utf.Anm;

namespace LibreLancer;

public class GameDataManager
{
    public ResourceManager Resources;
    private GameResourceManager? glResource;
    public GameItemDb Items;
    private Dictionary<string, string>? voicePathMap;
    private Dictionary<(string Voice, uint Hash), string>? looseVoiceLineMap;
    private Dictionary<string, string>? looseAudioPathMap;

    public FileSystem VFS => Items.VFS;

    private AnmFile? characterAnimations;

    public GameDataManager(GameItemDb items, ResourceManager resources)
    {
        Resources = resources;
        Items = items;
        glResource = Resources as GameResourceManager;
    }

    public void LoadData(IUIThread? ui, bool preloadCharacterAnimations = false, Action? onIniLoaded = null)
    {
        Items.LoadData(() =>
        {
            if (glResource != null && ui != null)
            {
                glResource.AddPreload(
                    Items.Ini.EffectShapes.Files.Select(txmfile => Items.DataPath(txmfile)).Where(x => x != null)
                        .OfType<string>()
                );

                foreach (var shape in Items.Ini.EffectShapes.Shapes)
                {
                    glResource.AddShape(shape.Key, shape.Value);
                }

                ui.QueueUIThread(() => glResource.Preload());
            }

            if (ui != null && onIniLoaded != null)
            {
                ui.QueueUIThread(onIniLoaded);
            }

            if (preloadCharacterAnimations)
            {
                GetCharacterAnimations();
            }
        });
    }

    public AnmFile GetCharacterAnimations()
    {
        if (characterAnimations != null)
        {
            return characterAnimations;
        }

        characterAnimations = new AnmFile();
        var stringTable = new StringDeduplication();

        foreach (var path in Items.Ini.Bodyparts.Animations.Select(file => Items.DataPath(file)).Where(x => x != null)
                     .OfType<string>())
        {
            using var stream = Items.VFS.Open(path);
            AnmFile.ParseToTable(characterAnimations.Scripts, characterAnimations.Buffer, stringTable, stream, path);
        }

        characterAnimations.Buffer.Commit();
        FLLog.Info("Anim", $"Character animations loaded: {characterAnimations.Scripts.Count} scripts from {Items.Ini.Bodyparts.Animations.Count} files");

        return characterAnimations;
    }

    public IEnumerable<Maneuver> GetManeuvers()
    {
        return Items.Ini.Hud.Maneuvers.Select(m => new Maneuver()
        {
            Action = m.Action,
            InfocardA = GetString(m.InfocardA),
            InfocardB = GetString(m.InfocardB),
            ActiveModel = m.ActiveModel,
            InactiveModel = m.InactiveModel,
        });
    }

    public ResolvedFx? ResolveEffect(string? nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return null;
        }

        return Items.ResolveFx(nickname);
    }

    public Texture2D? GetSplashScreen()
    {
        const string splashTextureFileName = "startupscreen.tga";
        const string splashTextureFileNameLarge = "startupscreen_1280.tga";

        if (glResource is null)
        {
            return null;
        }

        if (glResource.TextureExists(splashTextureFileNameLarge))
        {
            return Resources.FindTexture(splashTextureFileNameLarge) as Texture2D;
        }

        if (glResource.TextureExists(splashTextureFileName))
        {
            return Resources.FindTexture(splashTextureFileName) as Texture2D;
        }

        if (Items.VFS.FileExists(Items.Ini.Freelancer.DataPath + $"INTERFACE/INTRO/IMAGES/{splashTextureFileNameLarge}"))
        {
            glResource.AddTexture(
                splashTextureFileNameLarge,
                Items.DataPath($"INTERFACE/INTRO/IMAGES/{splashTextureFileNameLarge}")!
            );

            return glResource.FindTexture(splashTextureFileNameLarge) as Texture2D;
        }

        if (Items.VFS.FileExists(Items.Ini.Freelancer.DataPath + $"INTERFACE/INTRO/IMAGES/{splashTextureFileName}"))
        {
            glResource.AddTexture(
                splashTextureFileName,
                Items.DataPath($"INTERFACE/INTRO/IMAGES/{splashTextureFileName}")!
            );

            return glResource.FindTexture(splashTextureFileName) as Texture2D;
        }

        FLLog.Error("Splash", "Splash screen not found");
        return Resources.WhiteTexture;
    }

    private void PreloadSur(IDrawable dr, ResourceManager res)
    {
        if (dr is not IRigidModelFile rm)
        {
            return;
        }

        var mdl = rm.CreateRigidModel(res is GameResourceManager, res);
        var surpath = Path.ChangeExtension(mdl.Path, ".sur");
        if (!File.Exists(surpath))
        {
            return;
        }

        var cvx = res.ConvexCollection.UseFile(surpath);

        if (mdl.Source == RigidModelSource.SinglePart)
        {
            res.ConvexCollection.CreateShape(cvx, new ConvexMeshId(0, 0));
        }
        else
        {
            foreach (var p in mdl.AllParts)
                res.ConvexCollection.CreateShape(cvx, new ConvexMeshId(0, CrcTool.FLModelCrc(p.Name)));
        }
    }

    public void PreloadObjects(PreloadObject[]? objs, ResourceManager? resources = null)
    {
        resources ??= Resources;
        if (objs == null)
        {
            return;
        }

        foreach (var o in objs)
        {
            switch (o.Type)
            {
                case PreloadType.Ship:
                {
                    foreach (var v in o.Values)
                    {
                        var sh = Items.Ships.Get(v);
                        sh?.ModelFile?.LoadFile(resources);
                    }

                    break;
                }
                case PreloadType.Equipment:
                {
                    foreach (var v in o.Values)
                    {
                        var eq = Items.Equipment.Get(v);
                        eq?.ModelFile?.LoadFile(resources);
                    }

                    break;
                }
            }
        }
    }

    private bool cursorsDone = false;

    public void PopulateCursors()
    {
        if (cursorsDone)
        {
            return;
        }

        cursorsDone = true;

        Resources.LoadResourceFile(Items.DataPath(Items.Ini.Mouse.TxmFile!)!);

        foreach (var lc in Items.Ini.Mouse.Cursors)
        {
            var shape = Items.Ini.Mouse.Shapes.First(arg => arg.Name!.Equals(lc.Shape, StringComparison.OrdinalIgnoreCase));
            var cur = new Cursor
            {
                Nickname = lc.Nickname,
                Scale = lc.Scale,
                Spin = lc.Spin,
                Color = lc.Color,
                Hotspot = lc.Hotspot,
                Dimensions = shape.Dimensions,
                Texture = Items.Ini.Mouse.TextureName
            };

            glResource?.AddCursor(cur, cur.Nickname);
        }
    }

    public IEnumerable<Data.Schema.Audio.AudioEntry> AllSounds => Items.Ini.Audio.Entries;

    // Missing audio (Discovery references sounds it never shipped) is
    // looked up every emission otherwise - cache the misses and warn once.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> missingAudio =
        new(StringComparer.InvariantCultureIgnoreCase);

    public Data.Schema.Audio.AudioEntry? GetAudioEntry(string id)
    {
        if (missingAudio.ContainsKey(id))
        {
            return null;
        }
        var audio = Items.Ini.Audio.Entries.FirstOrDefault((arg) =>
            string.Equals(arg.Nickname, id, StringComparison.InvariantCultureIgnoreCase));

        if (audio != null)
        {
            return audio;
        }

        var fallback = GetLooseAudioPath(id);
        if (fallback != null)
        {
            return new Data.Schema.Audio.AudioEntry
            {
                Nickname = id,
                File = fallback,
                Type = Data.Schema.Audio.AudioType.Normal,
                Range = new Vector2(0, 2500),
                Attenuation = 0
            };
        }

        if (missingAudio.TryAdd(id, 0))
        {
            FLLog.Warning("Audio", $"Audio entry '{id}' not found");
        }
        return null;
    }

    public Stream? GetAudioStream(string id)
    {
        if (missingAudio.ContainsKey(id))
        {
            return null;
        }
        var audio = Items.Ini.Audio.Entries.FirstOrDefault((arg) =>
            string.Equals(arg.Nickname, id, StringComparison.InvariantCultureIgnoreCase));

        if (audio != null)
        {
            var path = Items.DataPath(audio.File);
            if (path != null && Items.VFS.FileExists(path))
            {
                return Items.VFS.Open(path);
            }
        }

        var fallback = GetLooseAudioPath(id);
        if (fallback != null && Items.VFS.FileExists(fallback))
        {
            return Items.VFS.Open(fallback);
        }

        if (missingAudio.TryAdd(id, 0))
        {
            FLLog.Warning("Audio", $"Audio entry '{id}' not found");
        }
        return null;

    }


    private void BuildLooseAudioPathMap()
    {
        if (looseAudioPathMap != null)
        {
            return;
        }

        looseAudioPathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var audioRoot = Items.Ini.Freelancer.DataPath + "AUDIO";

        void Scan(string folder)
        {
            foreach (var file in Items.VFS.GetFiles(folder))
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".utf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativePath = folder + "\\" + file;
                var key = Path.GetFileNameWithoutExtension(file);
                looseAudioPathMap.TryAdd(key, relativePath);
                looseAudioPathMap.TryAdd(key.Replace('-', '_'), relativePath);
            }

            foreach (var dir in Items.VFS.GetDirectories(folder))
            {
                Scan(folder + "\\" + dir);
            }
        }

        Scan(audioRoot);
    }

    private string? GetLooseAudioPath(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        BuildLooseAudioPathMap();
        if (looseAudioPathMap == null)
        {
            return null;
        }

        if (looseAudioPathMap.TryGetValue(id, out var direct))
        {
            return direct;
        }

        var normalized = id.Replace('-', '_');
        if (looseAudioPathMap.TryGetValue(normalized, out direct))
        {
            return direct;
        }

        return null;
    }

    private void BuildVoicePathMap()
    {
        if (voicePathMap != null && looseVoiceLineMap != null)
        {
            return;
        }

        voicePathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        looseVoiceLineMap = new Dictionary<(string Voice, uint Hash), string>();
        var audioRoot = Items.Ini.Freelancer.DataPath + "AUDIO";

        void Scan(string folder, string? voiceFolder)
        {
            foreach (var file in Items.VFS.GetFiles(folder))
            {
                var relativePath = folder + "\\" + file;
                var ext = Path.GetExtension(file);
                if (ext.Equals(".utf", StringComparison.OrdinalIgnoreCase))
                {
                    voicePathMap[Path.GetFileNameWithoutExtension(file)] = relativePath;
                }
                else if (voiceFolder != null && ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (TryParseVoiceHash(fileName, out var hash))
                    {
                        looseVoiceLineMap[(voiceFolder, hash)] = relativePath;
                    }
                }
            }

            foreach (var dir in Items.VFS.GetDirectories(folder))
            {
                var child = folder + "\\" + dir;
                Scan(child, voiceFolder ?? dir);
            }
        }

        Scan(audioRoot, null);
    }

    private static bool TryParseVoiceHash(string fileName, out uint hash)
    {
        if (fileName.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(fileName[2..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out hash);
        }

        return uint.TryParse(fileName, out hash);
    }

    public string? GetVoicePath(string id)
    {
        var direct = Items.Ini.Freelancer.DataPath + "AUDIO\\" + id + ".utf";
        if (Items.VFS.FileExists(direct))
        {
            return direct;
        }

        BuildVoicePathMap();
        if (voicePathMap!.TryGetValue(id, out var mapped))
        {
            return mapped;
        }

        return null;
    }

    public Stream? GetVoiceLineStream(string voice, uint hash)
    {
        BuildVoicePathMap();
        return looseVoiceLineMap!.TryGetValue((voice, hash), out var path) && Items.VFS.FileExists(path)
            ? Items.VFS.Open(path)
            : null;
    }

    public string? GetInfocardText(int id, FontManager fonts)
    {
        var res = Items.Ini.Infocards.GetXmlResource(id);
        return res == null ? null : Infocards.RDLParse.Parse(res, fonts).ExtractText();
    }

    public Infocards.Infocard GetInfocard(int id, FontManager fonts)
    {
        return Infocards.RDLParse.Parse(Items.Ini.Infocards.GetXmlResource(id), fonts);
    }

    public bool GetRelatedInfocard(int ogId, FontManager fonts, [MaybeNullWhen(false)] out Infocards.Infocard ic)
    {
        ic = null;

        if (!Items.Ini.InfocardMap.Map.TryGetValue(ogId, out int newId))
        {
            return false;
        }

        ic = GetInfocard(newId, fonts);
        return true;

    }

    public string GetString(int id)
    {
        return Items.Ini.Infocards.GetStringResource(id);
    }

    public IntroScene GetIntroScene()
    {
        // Golden-test determinism: autoplay runs always show the same intro,
        // otherwise menu screenshots can never be compared between backends.
        // SIRIUS_INTRO=N picks a specific scene for visual debugging.
        if (Environment.GetEnvironmentVariable("SIRIUS_INTRO") is { } forced &&
            int.TryParse(forced, out var forcedIndex) &&
            forcedIndex >= 0 && forcedIndex < Items.IntroScenes.Count)
        {
            return Items.IntroScenes[forcedIndex];
        }
        if (SiriusAutoplay.Enabled)
        {
            return Items.IntroScenes[0];
        }
        var rand = new Random();
        return Items.IntroScenes[rand.Next(0, Items.IntroScenes.Count)];
    }
#if DEBUG
        public IntroScene GetIntroSceneSpecific(int i)
        {
            if (i > Items.IntroScenes.Count)
                return null;
            return Items.IntroScenes[i];
        }
#endif

    public IEnumerator<object?> LoadSystemResources(StarSystem sys)
    {
        if (Items.Ini.Stars != null)
        {
            foreach (var txmFile in Items.Ini.Stars.TextureFiles
                         .SelectMany(x => x.Files))
            {
                Resources.LoadResourceFile(Items.DataPath(txmFile));
            }
        }

        yield return null;
        sys.StarsBasic?.LoadFile(Resources);
        sys.StarsComplex?.LoadFile(Resources);
        sys.StarsNebula?.LoadFile(Resources);
        yield return null;
        long a = 0;

        if (glResource != null)
        {
            foreach (var obj in sys.Objects)
            {
                obj.Archetype?.ModelFile?.LoadFile(glResource);
                if (a % 3 == 0)
                {
                    yield return null;
                }

                a++;
            }
        }

        foreach (var resFile in sys.ResourceFiles)
        {
            Resources.LoadResourceFile(resFile);
            if (a % 3 == 0)
            {
                yield return null;
            }

            a++;
        }
    }

    public void LoadAllSystem(StarSystem system)
    {
        var iterator = LoadSystemResources(system);

        while (iterator.MoveNext())
        {
        }
    }

    public (ModelResource?, float[]?) GetSolar(string solar)
    {
        var at = Items.Archetypes.Get(solar);
        return (at?.ModelFile?.LoadFile(Resources), at?.LODRanges);
    }

    public IDrawable? GetProp(string prop)
    {
        if (Items.Ini.PetalDb.Props.TryGetValue(prop, out var path))
        {
            return Resources.GetDrawable(Items.DataPath(path))?.Drawable;
        }

        FLLog.Error("PetalDb", "No prop exists: " + prop);
        return null;
    }

    public IDrawable? GetCart(string cart)
    {
        return Resources.GetDrawable(Items.DataPath(Items.Ini.PetalDb.Carts[cart]))?.Drawable;
    }

    public IDrawable? GetRoom(string room)
    {
        return Resources.GetDrawable(Items.DataPath(Items.Ini.PetalDb.Rooms[room]))?.Drawable;
    }

    public Dictionary<string, string> GetBaseNavbarIcons()
    {
        return Items.Ini.BaseNavBar.Navbar;
    }

    public string? GetCostumeForNPC(string npc)
    {
        return Items.Ini.SpecificNPCs.Npcs
            .FirstOrDefault(x => x.Nickname.Equals(npc, StringComparison.OrdinalIgnoreCase))
            ?.BaseAppr;
    }
}
