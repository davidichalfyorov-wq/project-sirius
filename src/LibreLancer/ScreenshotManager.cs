// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace LibreLancer
{
	public class ScreenshotManager
	{
        private FreelancerGame g;
        private string screenshotdir;
        private List<string> names = [];
		public ScreenshotManager(FreelancerGame game)
		{
            g = game;
			screenshotdir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "FreelancerShots");
		}
		public void TakeScreenshot()
		{
			if (!Directory.Exists(screenshotdir))
				Directory.CreateDirectory(screenshotdir);
			var dt = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);
			var name = Path.Combine(screenshotdir, "librelancer_" + dt);
			if (names.Contains(name))
			{
				name += " " + new Random().Next();
			}
			names.Add(name);
			// name already contains screenshotdir; combining again only
			// worked while MyPictures resolved to an absolute path.
			g.Screenshot(name + ".png");
		}

        public void Save(string filename, int width, int height, Bgra8[] data)
        {
            // Headless test runs (SIRIUS_AUTOPLAY) get killed right after the
            // capture - write synchronously there so the PNG always lands,
            // and surface encoder errors instead of losing them in a Task.
            if (SiriusAutoplay.Enabled)
            {
                try
                {
                    using var output = File.Create(filename);
                    ImageLib.PNG.Save(output, width, height, data, true);
                    FLLog.Info("Screenshot", $"Saved {filename}");
                }
                catch (Exception ex)
                {
                    FLLog.Error("Screenshot", $"Failed to save {filename}: {ex}");
                }
                return;
            }

            Task.Run(() =>
            {
                using var output = File.Create(filename);
                ImageLib.PNG.Save(output, width, height, data, true);
            });
        }
	}
}
