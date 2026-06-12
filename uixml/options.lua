require 'ids.lua'

local msaa_levels = {
	"NONE",
	"MSAA 2x",
	"MSAA 4x",
	"MSAA 8x"
}

// Post-process AA shares the ANTI-ALIASING selector with MSAA: the
// entries after the MSAA levels select post_aa instead (roadmap 4.7).
local post_aa_levels = {
	"FXAA",
	"SMAA"
}

// ini value for each post_aa_levels entry, by offset past msaamax
local post_aa_values = {
	"fxaa",
	"smaa"
}

local function post_aa_to_idx(s, msaamax)
{
	for (i in 1..post_aa_values.length) {
		if (s == post_aa_values[i]) return msaamax + i;
	}
	return 0
}

local function idx_to_post_aa(idx, msaamax)
{
	local i = idx - msaamax
	if (i >= 1 && i <= post_aa_values.length) return post_aa_values[i];
	return "off"
}

local function val_selection(left, right, display, values, vmin, vmax, vcurrent)
{
	local state = {}
	state.vmin = vmin
	state.vmax = vmax
	state.vcurrent = vcurrent
	state.values = values
	local function setval(idx)
	{
		local i = idx
		if (i < vmin) i = vmax;
		if (i > vmax) i = vmin;
		state.vcurrent = i
		display.Text = state.values[state.vcurrent]
	}
	left.OnClick(() => setval(state.vcurrent - 1));
	right.OnClick(() => setval(state.vcurrent + 1));
	setval(vcurrent)
	return state
}

// Map strings to MSAA Amounts
local function msaa_to_idx(i)
{
	if (i == 2) return 2;
	if (i == 4) return 3;
	if (i >= 8) return 4;
	return 1
}

local function idx_to_msaa(i)
{
	switch(i) {
		case 2: return 2;
		case 3: return 4;
		case 4: return 8;
		default: return 0;
	}
}

local tonemap_modes = {
	"OFF",
	"FILMIC",
	"ACES FILMIC"
}

local bloom_modes = {
	"OFF",
	"ON"
}

local function tonemapper_to_idx(s) => s == "aces" ? 3 : (s == "off" ? 1 : 2);

local function idx_to_tonemapper(i) => i == 3 ? "aces" : (i == 1 ? "off" : "filmic");

// Exposure slider: log scale, 0.25..4.0 with 1.0 at the middle
local function exposure_to_slider(e) => math.log(e / 0.25) / math.log(16);

local function slider_to_exposure(v) => 0.25 * math.pow(16, v);

// Anisotropy Levels
local function idx_to_anisotropy(i) => i == 1 ? 0 : math.pow(2, i - 1);

local function anisotropy_to_idx(i)
{
	if (i == 0) return 1;
	local x = 2
	for (j in 2..10) {
		if(x == i) return j;
		x *= 2;
	}
	return 1;
}

class options : options_Designer with Modal
{
	options()
	{
		base();
		local e = this.Elements
		this.isModal = false
		this.Elements.goback.OnClick(() => this.do_goback());
		this.Widget.OnEscape(() => this.do_goback());
		this.Panels = {
			{ e.performance, e.win_performance },
			{ e.audio, e.win_audio },
			{ e.controls, e.win_controls }
		}
		for (p in this.Panels)
			p[1].OnClick(() => this.panel(p));
		
		this.panel(this.Panels[1])
		this.opts = Game.GetCurrentSettings()
		e.sfxvol.Value = this.opts.SfxVolume
		e.voicevol.Value = this.opts.VoiceVolume
		this.keymap = Game.GetKeyMap()

		e.listtable.SetData(this.keymap)

		e.listtable.OnDoubleClick((row, column) => {

			local mk = new mapkey(this.keymap.GetKeyId(row), (reason) => {
				if (reason == 'cancel')
					this.keymap.CancelCapture();
				elseif (reason == 'clear')
					this.keymap.ClearCapture();
			});
			OpenModal(mk)
			this.keymap.CaptureInput(row, column != 2, (state, combo, key, accept) => {
				mk.Close('captured')
				if (state == 'overwrite')
					OpenModal(new alreadymapped(combo, key, (e) => { if (e == 'continue')  accept(); }));
			});

		});

		e.musicvol.Value = this.opts.MusicVolume

		this.AnisotropyLevels = this.opts.AnisotropyLevels()
		local anisotropy = { "NONE" }
		for (i in this.AnisotropyLevels)
			table.insert(anisotropy, tostring(i) + "x AF");
	
		// One ANTI-ALIASING selector: MSAA levels first, then post-AA modes
		local msaamax = msaa_to_idx(this.opts.MaxMSAA())
		local aa_values = {}
		for (i in 1..msaamax)
			table.insert(aa_values, msaa_levels[i]);
		for (v in post_aa_levels)
			table.insert(aa_values, v);
		local aa_current = post_aa_to_idx(this.opts.PostAA, msaamax)
		if (aa_current == 0) aa_current = msaa_to_idx(this.opts.MSAA);
		this.MSAAMax = msaamax
		this.MSAA = val_selection(e.msaa_left, e.msaa_right, e.msaa_display, aa_values, 1, aa_values.length, aa_current)
		this.AF = val_selection(e.af_left, e.af_right, e.af_display, anisotropy, 1, anisotropy.length, anisotropy_to_idx(this.opts.Anisotropy))
		this.Tonemap = val_selection(e.tonemap_left, e.tonemap_right, e.tonemap_display, tonemap_modes, 1, 3, tonemapper_to_idx(this.opts.Tonemapper))
		e.exposure.Value = exposure_to_slider(this.opts.Exposure)
		this.Bloom = val_selection(e.bloom_left, e.bloom_right, e.bloom_display, bloom_modes, 1, 2, this.opts.Bloom ? 2 : 1)
		e.bloom_intensity.Value = this.opts.BloomIntensity / 0.6
		this.GodRays = val_selection(e.godrays_left, e.godrays_right, e.godrays_display, bloom_modes, 1, 2, this.opts.GodRays ? 2 : 1)
		e.godrays_intensity.Value = this.opts.GodRaysIntensity / 0.8
		this.Ibl = val_selection(e.ibl_left, e.ibl_right, e.ibl_display, bloom_modes, 1, 2, this.opts.Ibl ? 2 : 1)
		this.Shadows = val_selection(e.shadows_left, e.shadows_right, e.shadows_display, bloom_modes, 1, 2, this.opts.Shadows ? 2 : 1)
		// RT toggles lock to OFF when the device has no ray query support
		local rtmax = this.opts.RayTracingSupported() ? 2 : 1
		this.RtShadows = val_selection(e.rtshadows_left, e.rtshadows_right, e.rtshadows_display, bloom_modes, 1, rtmax, (rtmax == 2 and this.opts.RtShadows) ? 2 : 1)
		this.Rtao = val_selection(e.rtao_left, e.rtao_right, e.rtao_display, bloom_modes, 1, rtmax, (rtmax == 2 and this.opts.Rtao) ? 2 : 1)
		this.RtReflections = val_selection(e.rtrefl_left, e.rtrefl_right, e.rtrefl_display, bloom_modes, 1, rtmax, (rtmax == 2 and this.opts.RtReflections) ? 2 : 1)
		local pipeline_modes = { "CLASSIC", "MESH SHADER" }
		local msmax = this.opts.MeshShadersSupported() ? 2 : 1
		this.MeshAsteroids = val_selection(e.meshast_left, e.meshast_right, e.meshast_display, pipeline_modes, 1, msmax, (msmax == 2 and this.opts.MeshAsteroids) ? 2 : 1)
		local vrsmax = this.opts.VrsSupported() ? 2 : 1
		this.Vrs = val_selection(e.vrs_left, e.vrs_right, e.vrs_display, bloom_modes, 1, vrsmax, (vrsmax == 2 and this.opts.Vrs) ? 2 : 1)

		this.controlcategories = { e.cat_ship, e.cat_ui, e.cat_mp }
		e.cat_ship.OnClick(() => this.setcontrolcategory(1))
		e.cat_ui.OnClick(() => this.setcontrolcategory(2))
		e.cat_mp.OnClick(() => this.setcontrolcategory(3))
		e.ctrl_default.OnClick(() => this.keymap.DefaultBindings())
		e.ctrl_cancel.OnClick(() => this.keymap.ResetBindings())
	}

	do_goback()
	{
		local e = this.Elements
		this.opts.SfxVolume = e.sfxvol.Value
		this.opts.MusicVolume = e.musicvol.Value
		this.opts.VoiceVolume = e.voicevol.Value
		if (this.MSAA.vcurrent > this.MSAAMax) {
			this.opts.MSAA = 0
			this.opts.PostAA = idx_to_post_aa(this.MSAA.vcurrent, this.MSAAMax)
		} else {
			this.opts.MSAA = idx_to_msaa(this.MSAA.vcurrent)
			this.opts.PostAA = "off"
		}
		this.opts.Anisotropy = idx_to_anisotropy(this.AF.vcurrent)
		this.opts.Tonemapper = idx_to_tonemapper(this.Tonemap.vcurrent)
		this.opts.Exposure = slider_to_exposure(e.exposure.Value)
		this.opts.Bloom = this.Bloom.vcurrent == 2
		this.opts.BloomIntensity = e.bloom_intensity.Value * 0.6
		this.opts.GodRays = this.GodRays.vcurrent == 2
		this.opts.GodRaysIntensity = e.godrays_intensity.Value * 0.8
		this.opts.Ibl = this.Ibl.vcurrent == 2
		this.opts.Shadows = this.Shadows.vcurrent == 2
		this.opts.RtShadows = this.RtShadows.vcurrent == 2
		this.opts.Rtao = this.Rtao.vcurrent == 2
		this.opts.RtReflections = this.RtReflections.vcurrent == 2
		this.opts.MeshAsteroids = this.MeshAsteroids.vcurrent == 2
		this.opts.Vrs = this.Vrs.vcurrent == 2
		this.keymap.Save();
		Game.ApplySettings(this.opts)
		if (this.isModal) {
			Game.Resume()
			this.Close()
		} else {
			OpenScene("mainmenu")
		}
	}
	asmodal()
	{
		this.ModalInit()
		this.Elements.fllogo.Visible = false
		this.Elements.backdrop.Visible = true
		this.Elements.goback.Strid = STRID_RETURN_TO_GAME
		this.isModal = true
		return this;
	}

	panel(p)
	{
		for(panel in this.Panels) {
			if(panel[1] == p[1]) {
				panel[1].Selected = true;
				panel[2].Visible = true;
			} else {
				panel[1].Selected = false;
				panel[2].Visible = false;
			}
		}
	}

	setcontrolcategory(cat)
	{
		this.keymap.SetGroup(cat - 1);
		for (index, value in ipairs(this.controlcategories)) {
			value.Selected = index == cat
		}
	}
}
