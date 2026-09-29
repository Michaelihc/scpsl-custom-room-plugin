"""Small primitive studies of the current SCP:SL Wiki renders.

See generated/schematics/warmup_station_stars/model-references.md for exact images.
Anatomical proportions and major silhouette features take priority over tiny detail.
+Z faces the aisle. This module never includes the separate observation creature.
"""
from scp_builder import Builder

BUDGETS = {'049':18,'079':14,'096':22,'106':22,'173':22,'939':28,'3114':28}


def build(role):
    b=Builder(f'scp{role}_root')
    def box(n,p,s,c,r=(0,0,0)): b.box(n,p,s,c,r)
    def oval(n,p,s,c,r=(0,0,0)): b.add(n,p,r,s,c,0)
    def limb(n,a,z,w,c,depth=None):
        b.seg(n,a,z,w,c,thick=depth or w,overrun=w*1.1)
        b.blocks[-1]['Properties']['PrimitiveType']=0
    def bone(n,a,z,w,c): b.seg(n,a,z,w,c,overrun=.025)
    skin,shadow,dark='#C8C3C1','#969093','#262326'
    if role=='049':
        # RenderSCP-049: hood/cowl, shoulder cape, long black split robe, bare mask.
        for s in (-1,1):
            oval('boot',(.18*s,.095,.12),(.25,.19,.40),'#1D2021')
            oval('robe_panel',(.16*s,.86,0),(.51,1.50,.55),'#343838',r=(0,0,s*2))
        oval('torso',(0,1.54,-.015),(.62,.67,.49),'#343838')
        oval('cape',(0,1.84,-.025),(.93,.32,.61),'#292D2E')
        oval('hood',(0,2.15,-.035),(.57,.70,.52),'#202425')
        oval('mask_head',(0,2.21,.188),(.36,.41,.24),'#A9AAA1')
        oval('beak',(0,2.02,.375),(.21,.52,.23),'#A9AAA1',(-49,0,0))
        for s in (-1,1):
            oval('eye',(.115*s,2.27,.309),(.09,.055,.045),'#202325',r=(0,0,s*9))
            limb('sleeve',(.36*s,1.81,.015),(.43*s,.96,.095),.23,'#343838')
            oval('glove',(.43*s,.83,.12),(.16,.30,.15),'#222526',r=(-8,0,s*6))
        head=(0,2.21,.188)
    elif role=='079':
        # RenderSCP-079: beige CRT and keyboard, black screen with the white X.
        box('base',(0,.10,-.04),(1.03,.20,.61),'#4C5046')
        box('monitor',(0,.75,-.09),(1.10,.91,.69),'#A69C7F')
        box('bezel',(0,.75,.276),(.97,.78,.055),'#5D5B4E')
        box('screen',(-.045,.78,.312),(.77,.62,.04),'#101517')
        for angle in (-42,42):
            box('screen_x',(-.045,.78,.342),(.085,.57,.024),'#DEE1D9',(0,0,angle))
        box('keyboard',(0,.225,.47),(1.18,.20,.50),'#B0A58C',(11,0,0))
        box('keys',(0,.329,.48),(.98,.025,.31),'#5D5B4E',(11,0,0))
        box('spacebar',(.02,.329,.642),(.46,.04,.04),'#B0A58C',(11,0,0))
        oval('knob',(.445,.47,.319),(.055,.055,.041),'#262A28')
        box('power',(.455,.405,.326),(.031,.025,.027),'#C58342')
        box('drive',(-.64,.115,.25),(.30,.21,.43),'#A69C7F')
        box('drive_slot',(-.64,.135,.467),(.22,.018,.02),'#30362F')
        head=(-.045,.78,.342)
    elif role=='096':
        # 096rerender: small bald head, exposed rib mass, long arms below the knees.
        for s in (-1,1):
            oval('foot',(.145*s,.065,.10),(.17,.13,.29),shadow)
            limb('shin',(.145*s,.13,0),(.12*s,.68,.065),.10,skin)
            limb('thigh',(.12*s,.68,.065),(.15*s,1.37,-.02),.14,skin)
        oval('hips',(0,1.38,-.02),(.39,.31,.27),shadow)
        oval('abdomen',(0,1.64,-.015),(.30,.50,.27),skin)
        oval('ribcage',(0,1.99,.02),(.59,.65,.36),skin,(10,0,0))
        oval('neck',(0,2.26,.065),(.26,.40,.28),shadow,(-22,0,0))
        oval('head',(0,2.43,.20),(.31,.47,.32),skin,(12,0,0))
        oval('mouth',(0,2.295,.351),(.15,.16,.05),'#554347',(12,0,0))
        for s in (-1,1):
            oval('eye_hollow',(.087*s,2.47,.355),(.10,.072,.045),'#71676D')
            limb('upper_arm',(.28*s,2.16,.015),(.47*s,1.43,.02),.135,skin)
            limb('forearm',(.47*s,1.43,.02),(.41*s,.70,.18),.115,skin)
            oval('hand',(.41*s,.57,.23),(.16,.29,.16),shadow,(-17,0,s*12))
        head=(0,2.43,.20)
    elif role=='106':
        # Scp10613: rotten bald skin, exposed abdomen, cropped black shoulder vest.
        rot,rotlight,cloth='#514C43','#726B5D','#202320'
        for s in (-1,1):
            oval('foot',(.145*s,.06,.085),(.18,.12,.30),rot)
            limb('shin',(.145*s,.12,0),(.16*s,.47,-.055),.135,rot)
            limb('thigh',(.16*s,.47,-.055),(.12*s,.89,-.06),.18,rot)
        oval('hips',(0,.88,-.065),(.39,.30,.29),rot)
        oval('abdomen',(0,1.095,-.04),(.31,.38,.27),rotlight)
        oval('chest',(0,1.34,-.03),(.52,.42,.32),rot)
        for s in (-1,1):
            box('vest',(.19*s,1.385,.04),(.19,.33,.29),cloth,(12,0,s*10))
            limb('upper_arm',(.285*s,1.42,-.03),(.36*s,1.04,.08),.15,rot)
            limb('forearm',(.36*s,1.04,.08),(.33*s,.73,.26),.12,rotlight)
            oval('hand',(.33*s,.665,.30),(.14,.22,.14),rot,(-24,0,0))
        oval('head',(0,1.67,.15),(.27,.39,.29),rotlight,(10,0,0))
        for s in (-1,1):
            oval('eye',(.071*s,1.699,.286),(.056,.048,.035),'#B0AB8B')
        oval('grin',(0,1.565,.29),(.17,.063,.04),'#BBB393')
        head=(0,1.67,.15)
    elif role=='173':
        # RenderSCP-173 (Matthew): asymmetric fused concrete masses and pointed legs.
        concrete,wear,red,green='#A08E69','#B2A17F','#794947','#577C43'
        oval('leg_left',(-.25,.39,.02),(.40,.80,.40),'#6B5946',(0,0,-9))
        oval('leg_right',(.22,.41,-.04),(.39,.84,.41),'#78664F',(0,0,8))
        oval('body',(0,.97,-.015),(.92,1.08,.63),concrete,(0,0,-9))
        oval('side_lobe',(-.35,1.43,-.09),(.61,.77,.56),concrete,(0,0,-21))
        oval('main_lobe',(.18,1.69,.01),(.73,.79,.65),wear,(0,0,15))
        oval('chest_lobe',(.01,1.26,.32),(.53,.58,.35),wear,(0,0,-13))
        limb('arm_hook',(-.43,1.05,.02),(-.55,.75,.19),.26,concrete)
        limb('thorn',(.30,1.17,0),(.79,1.01,.16),.15,concrete)
        limb('thorn',(.27,.98,.015),(.70,.74,.18),.12,wear)
        # Three markings on the main and chest lobes, matching their green/red read.
        for name,x,y,z,scale in [('main',.18,1.70,.327,1),('chest',.01,1.26,.497,.72)]:
            oval(name+'_scar',(x,y,z),(.10*scale,.48*scale,.045),red,(0,0,11))
            oval(name+'_cross',(x,y-.01,z+.01),(.45*scale,.095*scale,.04),red,(0,0,-12))
            for s in (-1,1):
                oval(name+'_green',(x+s*.17*scale,y+.13*scale,z-.01),(.13*scale,.14*scale,.065),green,(0,0,s*16))
        oval('side_green',(-.56,1.46,.11),(.13,.20,.10),green,(0,-24,-15))
        head=(.18,1.69,.01)
    elif role=='939':
        # SCP-939tooth: current humanoid 939-168, no tail or legacy dog body.
        flesh,shade,crest='#813F37','#582F2C','#AA4C3B'
        for s in (-1,1):
            oval('foot',(.23*s,.06,.10),(.22,.12,.37),shade)
            limb('shin',(.23*s,.12,0),(.34*s,.61,-.13),.17,flesh)
            limb('thigh',(.34*s,.61,-.13),(.19*s,1.13,-.06),.25,flesh)
        oval('hips',(0,1.09,-.07),(.54,.36,.39),flesh)
        oval('abdomen',(0,1.34,-.07),(.41,.43,.32),shade)
        oval('chest',(0,1.64,-.02),(.76,.61,.46),flesh,(-12,0,0))
        oval('head',(0,1.84,.29),(.49,.38,.55),flesh,(-14,0,0))
        oval('maw',(0,1.77,.544),(.35,.24,.09),'#241B1B')
        oval('muzzle',(0,1.91,.53),( .37,.14,.21),'#A5896E')
        oval('lower_jaw',(0,1.63,.48),(.34,.13,.29),shade,(-8,0,0))
        for x in (-.12,.12):
            oval('fang_row',(x,1.765,.589),(.047,.23,.06),'#D85B31',(0,0,-x*65))
        for x,y,z in [(-.10,2.12,.02),(.10,2.10,-.06)]:
            oval('head_spine',(x,y,z),(.045,.55,.075),crest,(-24,0,-15))
        for sign in (-1,1):
            limb('upper_arm',(.35*sign,1.70,.01),(.47*sign,1.24,.01),.19,flesh)
            limb('forearm',(.47*sign,1.24,.01),(.48*sign,.88,.21),.14,flesh)
            oval('claw_hand',(.48*sign,.77,.27),(.19,.30,.15),shade,(-25,0,sign*12))
            for offset in (-.055,.055):
                oval('finger_claw',(.48*sign+offset,.61,.37),(.055,.26,.055),shade,(-30,0,sign*10))
        head=(0,1.84,.29)
    elif role=='3114':
        # SCP-3114_V2: normal rounded skull, narrow limbs, open rib cage and pelvis.
        bonecol,old,ink='#BCAD92','#8F7868','#242326'
        for s in (-1,1):
            oval('foot',(.12*s,.045,.10),(.14,.09,.27),old)
            bone('shin',(.12*s,.09,0),(.13*s,.64,-.015),.06,bonecol)
            bone('thigh',(.13*s,.64,-.015),(.12*s,1.19,0),.075,bonecol)
            oval('pelvis_wing',(.13*s,1.23,-.01),(.20,.26,.15),old,(0,0,s*28))
        bone('pelvis_bridge',(-.13,1.17,.025),(.13,1.17,.025),.065,old)
        bone('spine',(0,1.21,-.075),(0,1.99,-.07),.06,old)
        for i,width in enumerate((.17,.22,.25)):
            for sign in (-1,1):
                bone('rib',(0,1.48+i*.14,.115),(width*sign,1.53+i*.14,-.03),.048,bonecol)
        bone('sternum',(0,1.48,.12),(0,1.84,.12),.065,bonecol)
        oval('skull',(0,2.12,0),(.29,.37,.28),bonecol)
        oval('jaw',(0,1.965,.04),(.20,.14,.19),old)
        for sign in (-1,1):
            oval('eye_socket',(.075*sign,2.13,.13),(.10,.10,.046),ink)
            bone('upper_arm',(.27*sign,1.80,0),(.30*sign,1.46,-.01),.062,bonecol)
            bone('forearm',(.30*sign,1.46,-.01),(.34*sign,1.10,.10),.051,bonecol)
            oval('hand',(.34*sign,1.04,.13),(.09,.18,.075),old,(-15,0,0))
        oval('nose',(0,2.035,.14),(.04,.055,.03),ink)
        head=(0,2.12,0)
    else: raise ValueError(role)
    b.marker('marker_pivot',(0,0,0))
    b.marker('marker_screen' if role=='079' else 'marker_head',head)
    assert b.visible_count()<=BUDGETS[role],(role,b.visible_count())
    return b
