"""Angular collectible SCP figures; all recipes are final, game-ready primitives.

Deliberately enlarged identifying features, offset stances, ivory/charcoal surfaces
and restrained warm accents. No inherited mannequin geometry or post-hoc reshaping.
All models face +Z and their visible bases meet y=0. Creature sculpture is separate.
"""
from scp_builder import Builder


def build(role):
    b = Builder(f'scp{role}_root')
    def box(n,p,s,c,r=(0,0,0)): b.box(n,p,s,c,r)
    def oval(n,p,s,c,r=(0,0,0)): b.add(n,p,r,s,c,0)
    def bone(n,a,z,w,c): b.seg(n,a,z,w,c)
    ivory, shade, ink = '#E8DCC4', '#A79586', '#242638'
    if role == '049':
        # Tailored trapezoid read: faceted skirt, broad collar, tilted large mask.
        b.cylinder('robe_skirt',(0,.40,0),(.86,.40,.65),'#282B42',rot=(0,22.5,0))
        box('robe_body',(0,1.05,-.02),(.55,.93,.43),'#363B53',(-5,0,0))
        box('collar',(0,1.48,.015),(.83,.23,.49),'#171D30',(0,0,-5))
        box('mask_head',(.025,1.89,.06),(.50,.55,.46),ivory,(0,0,-8))
        oval('beak',(.02,1.73,.49),(.19,.65,.20),ivory,(-62,0,0))
        for s in (-1,1):
            box('lens',(.15*s,1.94,.305),(.12,.13,.048),ink,(0,0,-8))
        b.cylinder('hat_brim',(.025,2.16,.02),(.94,.035,.78),'#151A2A',rot=(0,0,-8))
        box('hat_crown',(.057,2.31,.01),(.51,.27,.42),'#282B42',(0,0,-8))
        bone('sleeve_left',(-.35,1.43,0),(-.48,.93,.15),.22,'#363B53')
        bone('sleeve_right',(.35,1.43,0),(.43,1.12,.36),.22,'#363B53')
        box('glove_left',(-.48,.88,.18),(.18,.20,.20),'#171D30',(8,0,0))
        box('glove_right',(.43,1.08,.39),(.18,.19,.22),'#171D30',(-25,0,0))
        box('clasp',(0,1.39,.263),(.13,.13,.035),'#C89C59',(0,0,45))
        head=(.025,1.89,.06)
    elif role == '079':
        # Compact beige CRT; chunky pixel expression is geometry, not a texture.
        box('base',(0,.12,0),(1.10,.24,.70),'#9A8B80')
        box('monitor',(0,.69,-.03),(1.04,.92,.65),'#D6C6AC')
        box('monitor_rear',(0,.70,-.33),(.78,.69,.28),'#9A8B80',(0,0,0))
        box('bezel',(0,.73,.307),(.89,.69,.052),ink)
        box('screen',(0,.75,.342),(.75,.53,.04),'#345851')
        for s in (-1,1):
            box('pixel_eye',(.17*s,.82,.368),(.15,.055,.023),'#CBE6AD')
        box('pixel_mouth',(0,.64,.369),(.30,.04,.025),'#CBE6AD')
        box('keyboard',(0,.20,.51),(.96,.12,.40),'#D6C6AC',(12,0,0))
        box('keys',(0,.275,.50),(.76,.025,.24),'#69646A',(12,0,0))
        box('led',(.40,.44,.35),(.048,.047,.033),'#E38D55')
        head=(0,.75,.38)
    elif role == '096':
        # Knobby knees, sloping shoulders and hands drawn up beside the face.
        for s in (-1,1):
            box('foot',(.19*s,.065,.10),(.20,.13,.34),shade,(0,s*8,0))
            bone('shin',(.19*s,.13,.02),(.24*s,1.18,.09),.115,ivory)
        box('hips',(0,1.21,-.035),(.41,.22,.29),shade,(10,0,0))
        oval('torso',(0,1.68,.035),(.40,.89,.32),ivory,(12,0,0))
        box('shoulders',(0,2.04,.12),(.74,.15,.30),shade,(9,0,-6))
        box('head',(0,2.33,.20),(.34,.45,.32),ivory,(10,0,-5))
        box('mouth',(0,2.23,.378),(.17,.21,.045),'#493B42',(10,0,0))
        for s in (-1,1):
            bone('upper_arm',(.33*s,2.04,.12),(.48*s,1.47,.32),.105,ivory)
            bone('forearm',(.48*s,1.47,.32),(.27*s,2.17,.46),.095,ivory)
            box('hand',(.27*s,2.24,.43),(.11,.30,.12),shade,(0,0,-s*12))
            box('eye_hollow',(.10*s,2.43,.372),(.07,.045,.032),'#7A6668',(0,0,-5))
        head=(0,2.33,.20)
    elif role == '106':
        # A crooked charcoal coat and one reaching hand emerge from a tar pool.
        oval('tar_shadow',(0,.035,.08),(1.12,.07,.91),'#191B2C')
        for s in (-1,1):
            box('foot',(.18*s,.115,.13),(.24,.17,.35),'#353344',(0,s*10,0))
            bone('leg',(.18*s,.16,.04),(.14*s,.73,-.06),.18,'#484455')
        box('hips',(0,.71,-.04),(.49,.29,.35),'#353344',(0,0,-7))
        box('torso',(0,1.07,.045),(.52,.60,.37),'#59505A',(16,0,-7))
        box('back_hunch',(-.04,1.37,-.07),(.65,.31,.43),'#353344',(12,0,-7))
        box('head',(.04,1.41,.29),(.40,.41,.36),'#8A7976',(5,0,-9))
        for s in (-1,1):
            box('eye',(.04+.10*s,1.48,.49),(.085,.07,.035),ink,(0,0,-9))
        box('grin',(.04,1.34,.493),(.21,.048,.035),'#C2B49D',(0,0,-9))
        bone('arm_left',(-.29,1.28,.01),(-.43,.66,.20),.17,'#59505A')
        bone('arm_right',(.27,1.25,.02),(.36,1.10,.55),.18,'#59505A')
        box('hand_left',(-.43,.62,.22),(.17,.23,.16),'#8A7976')
        box('hand_right',(.36,1.08,.65),(.18,.15,.27),'#8A7976',(-12,0,0))
        head=(.04,1.41,.29)
    elif role == '173':
        # Brutalist concrete totem: heavy angular head, short supports, painted face.
        for s in (-1,1):
            box('foot',(.19*s,.21,0),(.25,.42,.34),'#9B8B78',(0,s*8,0))
        box('torso',(0,.73,0),(.62,.76,.46),'#C2AF92',(0,0,-6))
        box('shoulders',(0,1.05,.01),(.73,.27,.51),'#D9C7A9',(0,0,-6))
        box('head',(.035,1.48,.015),(.70,.72,.53),'#D9C7A9',(0,0,-6))
        for s in (-1,1):
            bone('arm',(.36*s,1.04,.04),(.41*s,.75,.35),.19,'#9B8B78')
        box('face',(.035,1.47,.294),(.43,.40,.045),'#9D4B42',(0,0,-6))
        for s in (-1,1):
            box('eye',(.035+.12*s,1.57,.324),(.095,.085,.035),'#26382F',(0,0,-6))
        box('mouth',(.035,1.37,.324),(.21,.045,.036),'#512D31',(0,0,-6))
        box('paint_slash',(-.18,1.73,.289),(.055,.21,.042),'#9D4B42',(0,0,-18))
        box('stain',(.11,.62,.241),(.12,.31,.035),'#867C64',(0,0,-12))
        head=(.035,1.48,.015)
    elif role == '939':
        # Eyeless wedge head; crouched, jointed limbs replace the old four stumps.
        oval('body',(0,.59,-.16),(.69,.63,1.39),'#8B343D')
        box('spine',(0,.84,-.22),(.31,.16,1.27),'#B25151',(6,0,0))
        for s in (-1,1):
            for z,knee in ((.35,.56),(-.69,-.85)):
                bone('upper_leg',(.25*s,.63,z),(.43*s,.34,knee),.19,'#8B343D')
                bone('lower_leg',(.43*s,.34,knee),(.43*s,.075,knee+.20),.15,'#532A37')
        box('head',(0,.75,.78),(.61,.42,.73),'#B25151',(-12,0,0))
        box('upper_jaw',(0,.86,1.14),(.56,.19,.52),'#B25151',(-12,0,0))
        box('maw',(0,.64,1.17),(.47,.30,.34),'#302333')
        box('lower_jaw',(0,.46,1.14),(.52,.16,.51),'#532A37',(8,0,0))
        box('teeth_top',(0,.738,1.383),(.42,.09,.06),'#E8DCC4',(-12,0,0))
        box('teeth_bottom',(0,.545,1.383),(.40,.075,.06),'#E8DCC4',(8,0,0))
        oval('tail',(0,.40,-1.12),(.22,.22,.88),'#8B343D',(-18,0,0))
        head=(0,.75,.78)
    elif role == '3114':
        # Angular oversized skull and a jaunty rib cage: a little anatomical puppet.
        for s in (-1,1):
            box('foot',(.14*s,.055,.12),(.17,.11,.30),shade,(0,s*8,0))
            bone('leg',(.14*s,.11,0),(.12*s,1.01,-.02),.075,ivory)
        box('pelvis',(0,1.05,0),(.37,.22,.22),shade,(0,0,-7))
        bone('spine',(0,1.05,-.05),(.045,1.77,-.04),.065,shade)
        for i,w in enumerate((.37,.46,.56)):
            box('rib',(.015,1.29+i*.16,.045),(w,.065,.14),ivory,(0,0,-7))
        box('skull',(.035,2.02,.015),(.46,.46,.36),ivory,(0,0,-7))
        box('jaw',(.05,1.80,.08),(.29,.15,.25),shade,(0,0,-7))
        for s in (-1,1):
            box('eye_socket',(.035+.115*s,2.08,.205),(.13,.13,.045),ink,(0,0,-7))
            bone('upper_arm',(.29*s,1.65,0),(.38*s,1.32,.03),.07,ivory)
            bone('forearm',(.38*s,1.32,.03),(.32*s,1.02,.20),.065,ivory)
            box('hand',(.32*s,1.00,.23),(.10,.17,.085),shade,(-20,0,0))
        box('nose',(.035,1.94,.211),(.065,.065,.038),ink,(0,0,38))
        head=(.035,2.02,.015)
    else:
        raise ValueError(role)
    b.marker('marker_pivot',(0,0,0))
    b.marker('marker_screen' if role=='079' else 'marker_head',head)
    return b
