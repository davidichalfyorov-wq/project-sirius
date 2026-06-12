// Implements widget as a child that can be opened + closed

local function _Lerp(x, y, t) {
	if (t >= 1) return y;
	if (t <= 0) return x;
	return x + (y - x) * t
}

// UI 2.0: AAA window motion - open springs out with a slight overshoot,
// close accelerates inward.
local function _EaseOutBack(t) {
	if (t >= 1) return 1;
	if (t <= 0) return 0;
	local c1 = 1.70158;
	local c3 = c1 + 1;
	local u = t - 1;
	return 1 + c3 * u * u * u + c1 * u * u;
}

local function _EaseInCubic(t) {
	if (t >= 1) return 1;
	if (t <= 0) return 0;
	return t * t * t;
}

mixin ChildWindow
{
    ChildWindowInit()
    {
        this.Opened = false;
        this.BkWidth = this.Elements.background.Width;
        this.BkHeight = this.Elements.background.Height;
        this.Widget.OnUpdate((delta) => this.Update(delta));
		this.Widget.OnEscape(() => this.Close());
    }
    
    Open(widget)
    {
        if(this.Opened) return;
        if(this.OnChildOpen) {
            this.OnChildOpen();
        }
        this.Opened = true;
        this.Parent = widget;
        this.AnimateIn();
        widget.AddChild(this.Widget);
    }
    
    AnimateIn()
    {
        PlaySound('ui_motion_swish')
	    this.Time = 0
	    this.Duration = 0.28
	    this.AnimatingIn = true
	    this.Elements.contents.Visible = false
	    this.Elements.background.Width = 0
	    this.Elements.background.Height = 0
    }

    AnimateOut(cb)
    {
        PlaySound('ui_motion_swish')
	    this.OutCallback = cb
	    this.Time = 0
	    this.Duration = 0.16
	    this.AnimatingOut = true
	    this.Elements.contents.Visible = false
    }

    Update(delta)
    {
        if (this.AnimatingIn) {
		    this.Time += delta
		    local t = _EaseOutBack(this.Time / this.Duration)
		    this.Elements.background.Width = this.BkWidth * t
		    this.Elements.background.Height = this.BkHeight * t
		    if (this.Time > this.Duration) {
			    this.Elements.background.Width = this.BkWidth
			    this.Elements.background.Height = this.BkHeight
			    this.Elements.contents.Visible = true
			    this.AnimatingIn = false
			    PlaySound('ui_window_open')
			    if (this.OnOpen != nil) {
					this.OnOpen();
				}
		    }
	    }
	    if (this.AnimatingOut) {
		    this.Time += delta
		    local t = 1 - _EaseInCubic(this.Time / this.Duration)
		    this.Elements.background.Width = this.BkWidth * t
	    	this.Elements.background.Height = this.BkHeight * t
	    	if (this.Time > this.Duration) {
	    		this.AnimatingOut = false
	    		this.Opened = false
	    		this.Parent.RemoveChild(this.Widget)
	    		if (this.OnClose) this.OnClose();
	    		if (this.OutCallback) {
	    			this.OutCallback()
	    			this.OutCallback = nil
	    		}
	    	}
	    }
    }
    
    Close(cb)
    {
        if (this.Opened && !this.AnimatingOut)
        {
        	if (this.Closing) this.Closing();
			this.AnimateOut(cb);
        }
    }
}


