import math, re, sys

def oklch_to_hex(L, C, h, alpha=None):
    a = C*math.cos(math.radians(h)); b = C*math.sin(math.radians(h))
    l_ = L + 0.3963377774*a + 0.2158037573*b
    m_ = L - 0.1055613458*a - 0.0638541728*b
    s_ = L - 0.0894841775*a - 1.2914855480*b
    l, m, s = l_**3, m_**3, s_**3
    r = +4.0767416621*l - 3.3077115913*m + 0.2309699292*s
    g = -1.2684380046*l + 2.6097574011*m - 0.3413193965*s
    bl = -0.0041960863*l - 0.7034186147*m + 1.7076147010*s
    def enc(x):
        x = max(0.0, min(1.0, x))
        return 12.92*x if x <= 0.0031308 else 1.055*(x**(1/2.4)) - 0.055
    rgb = [round(enc(v)*255) for v in (r, g, bl)]
    hx = ''.join('%02X' % v for v in rgb)
    if alpha is not None:
        return '#%02X%s' % (round(alpha*255), hx)
    return '#' + hx

# name -> (css, usage)
T = [
 ('# Surfaces', None),
 ('AppBackground', 'oklch(0.88 0.006 250)'),
 ('Surface', '#fff'),
 ('SurfaceStrip', 'oklch(0.97 0.003 250)'),
 ('SurfaceHeader', 'oklch(0.96 0.003 250)'),
 ('SurfaceExpanded', 'oklch(0.97 0.003 250)'),
 ('SurfaceHover', 'oklch(0.98 0.002 250)'),
 ('SurfaceSelected', 'oklch(0.94 0.03 235)'),
 ('SurfaceControlHover', 'oklch(0.94 0.003 250)'),
 ('SurfaceTitleHover', 'oklch(0.9 0.005 250)'),
 ('SurfaceCloseHover', 'oklch(0.93 0.1 25)'),
 ('Scrim', 'oklch(0.2 0.01 250 / 0.45)'),
 ('ScrimOnboarding', 'oklch(0.2 0.01 250 / 0.5)'),
 ('# Sidebar', None),
 ('SidebarBackground', 'oklch(0.20 0.015 255)'),
 ('SidebarActive', 'oklch(0.27 0.02 255)'),
 ('SidebarBorder', 'oklch(0.30 0.015 255)'),
 ('SidebarText', 'oklch(0.92 0.005 255)'),
 ('SidebarNavText', 'oklch(0.75 0.01 255)'),
 ('SidebarMuted', 'oklch(0.6 0.01 255)'),
 ('SidebarMutedHover', 'oklch(0.85 0.01 255)'),
 ('SidebarMothBody', 'oklch(0.55 0.02 255)'),
 ('# Text', None),
 ('Text', 'oklch(0.22 0.01 250)'),
 ('TextStrong', 'oklch(0.2 0.01 250)'),
 ('Text2', 'oklch(0.4 0.01 250)'),
 ('Text3', 'oklch(0.45 0.01 250)'),
 ('Muted', 'oklch(0.5 0.01 250)'),
 ('Muted2', 'oklch(0.55 0.01 250)'),
 ('Faint', 'oklch(0.6 0.01 250)'),
 ('Disabled', 'oklch(0.65 0.005 250)'),
 ('# Borders', None),
 ('BorderCard', 'oklch(0.9 0.005 250)'),
 ('BorderHeader', 'oklch(0.92 0.005 250)'),
 ('BorderInput', 'oklch(0.88 0.005 250)'),
 ('BorderButton', 'oklch(0.85 0.005 250)'),
 ('BorderSoft', 'oklch(0.94 0.003 250)'),
 ('BorderRow', 'oklch(0.93 0.003 250)'),
 ('BorderTransport', 'oklch(0.75 0.01 250)'),
 ('Connector', 'oklch(0.65 0.01 250)'),
 ('Scrollbar', 'oklch(0.75 0.01 250)'),
 ('# Accent and semantic', None),
 ('Accent', 'oklch(0.62 0.14 235)'),
 ('AccentHover', 'oklch(0.57 0.14 235)'),
 ('AccentPressed', 'oklch(0.52 0.14 235)'),
 ('AccentLink', 'oklch(0.5 0.14 235)'),
 ('AccentLinkStrong', 'oklch(0.55 0.16 235)'),
 ('AccentTint', 'oklch(0.93 0.04 235)'),
 ('AccentChip', 'oklch(0.93 0.05 235)'),
 ('AccentChipText', 'oklch(0.35 0.12 235)'),
 ('AccentChipBorder', 'oklch(0.8 0.08 235)'),
 ('AccentDot', 'oklch(0.55 0.16 235)'),
 ('DotOff', 'oklch(0.8 0.005 250)'),
 ('ButtonDisabledBg', 'oklch(0.9 0.005 250)'),
 ('ButtonDisabledText', 'oklch(0.6 0.005 250)'),
 ('Positive', 'oklch(0.5 0.14 145)'),
 ('Danger', 'oklch(0.55 0.15 25)'),
 ('DangerTint', 'oklch(0.95 0.03 25)'),
 ('DangerText', 'oklch(0.4 0.14 25)'),
 ('Warning', 'oklch(0.6 0.15 40)'),
 ('Unread', 'oklch(0.6 0.2 25)'),
 ('Recording', 'oklch(0.55 0.2 25)'),
 ('Overdub', 'oklch(0.7 0.14 55)'),
 ('LabelYellow', 'oklch(0.85 0.17 90)'),
 ('# Connection states', None),
 ('DisconnectedDot', 'oklch(0.6 0.2 25)'),
 ('DisconnectedBg', 'oklch(0.95 0.03 25)'),
 ('DisconnectedText', 'oklch(0.4 0.14 25)'),
 ('ConnectingDot', 'oklch(0.75 0.15 70)'),
 ('ConnectingBg', 'oklch(0.95 0.05 70)'),
 ('ConnectingText', 'oklch(0.42 0.12 70)'),
 ('ConnectedDot', 'oklch(0.6 0.16 145)'),
 ('ConnectedBg', 'oklch(0.93 0.06 145)'),
 ('ConnectedText', 'oklch(0.32 0.09 145)'),
 ('# Monitor console', None),
 ('ConsoleBackground', 'oklch(0.16 0.005 250)'),
 ('ConsoleText', 'oklch(0.85 0.01 145)'),
]

pat = re.compile(r'oklch\(\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*(?:/\s*([\d.]+))?\s*\)')
out = []
out.append('<ResourceDictionary xmlns="https://github.com/avaloniaui"')
out.append('                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">')
out.append('  <!-- GENERATED from the oklch() values in docs/design_handoff_avalonia_ui (see JoinFS.UI/util/gen_tokens.py).')
out.append('       Avalonia cannot parse oklch(), so each token holds the sRGB hex and keeps the design value in its comment.')
out.append('       Edit the oklch value in the table in gen_tokens.py and regenerate, so the two never drift apart. -->')
for name, css in T:
    if css is None:
        out.append('')
        out.append('  <!-- %s -->' % name[2:])
        continue
    if css == '#fff':
        hx = '#FFFFFF'
    else:
        m = pat.fullmatch(css)
        L, C, h, a = m.groups()
        hx = oklch_to_hex(float(L), float(C), float(h), float(a) if a else None)
    out.append('  <SolidColorBrush x:Key="%sBrush" Color="%s" /> <!-- %s -->' % (name, hx, css))
out.append('')
out.append('  <!-- Shadows. Modals and cards: 0 20px 50px -10px oklch(0.2 0.01 250 / 0.4). The shell: 0 24px 60px -12px oklch(0.2 0.01 250 / 0.35). -->')
out.append('  <BoxShadows x:Key="ModalShadow">0 20 50 -10 %s</BoxShadows>' % oklch_to_hex(0.2, 0.01, 250, 0.4))
out.append('  <BoxShadows x:Key="ShellShadow">0 24 60 -12 %s</BoxShadows>' % oklch_to_hex(0.2, 0.01, 250, 0.35))
out.append('')
out.append('  <!-- Accent as a Color too, for Fluent palette overrides -->')
out.append('  <Color x:Key="AccentColor">%s</Color>' % oklch_to_hex(0.62,0.14,235))
out.append('</ResourceDictionary>')
open(sys.argv[1], 'w', encoding='utf-8').write('\n'.join(out) + '\n')
print('ok', len(T))
