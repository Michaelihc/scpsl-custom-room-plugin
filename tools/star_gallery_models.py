"""Authored silhouette revision for the seven cheap gallery models.

Applied by the owning builders before export. Keeps every component/marker and the
original primitive budget; rounded masses and asymmetric poses replace the old
straight box mannequins. The separate observation-deck creature is not an SCP asset.
"""
from scp_builder import v3


def remodel(builder, role):
    def shape(prefix, primitive=None, size=None, rotation=None, offset=None):
        for b in builder.blocks:
            if b['Name'] != prefix and not b['Name'].startswith(prefix + '_'):
                continue
            if primitive is not None:
                b['Properties']['PrimitiveType'] = primitive
            if size is not None:
                b['Scale'] = v3(*size)
            if rotation is not None:
                b['Rotation'] = v3(*rotation)
            if offset is not None:
                for axis, amount in zip('xyz', offset):
                    b['Position'][axis] += amount

    if role == '049':
        # Bell-shaped coat, oval mask and a tapered, swept bird beak.
        shape('robe_skirt', 0, (1.08, 1.12, .82), offset=(0, .13, 0))
        shape('robe_body', 0, (.75, 1.30, .57))
        shape('shoulders', 0, (.94, .38, .58))
        shape('sleeve', 0, (.25, 1.02, .38), rotation=(8, 0, -8))
        shape('mask_head', 0, (.44, .51, .43))
        shape('beak', 0, (.16, .76, .20), rotation=(-65, 0, 0), offset=(0, -.025, .03))
        shape('hat_crown', size=(.51, .19, .46), rotation=(0, 0, -6))
    elif role == '079':
        # A squat CRT with a protruding sloped keyboard and deep rear housing.
        shape('base', size=(1.10, .49, .86), offset=(0, -.055, 0))
        shape('base_top', size=(1.02, .12, .53), rotation=(12, 0, 0), offset=(0, -.12, .31))
        shape('monitor', size=(1.16, .91, .83), offset=(0, -.10, -.01))
        shape('monitor_top', size=(1.03, .12, .68), offset=(0, -.10, -.045))
        for prefix in ('bezel', 'screen', 'led', 'marker_screen'):
            shape(prefix, offset=(0, -.10, 0))
        shape('screen', size=(.76, .48, .065))
    elif role == '096':
        # Hollow, emaciated upper body, drooping shoulders and elongated fingers.
        shape('torso', 0, (.48, 1.02, .36))
        shape('hips', 0, (.44, .28, .30))
        shape('shoulders', 0, (.78, .28, .36))
        shape('arm', 0, (.15, 2.03, .18), rotation=(4, 0, 0))
        shape('hand', size=(.14, .33, .19), offset=(0, -.025, -.035))
        shape('head', 0, (.30, .32, .30))
        shape('mouth', 0, (.19, .20, .10), offset=(0, .015, -.035))
    elif role == '106':
        # Eroded round shoulders, a projecting skull and an uneven reaching pose.
        shape('tar_shadow', 0, (1.08, .06, .92))
        shape('hips', 0, (.55, .39, .40))
        shape('torso', 0, (.61, .79, .48), rotation=(16, 0, -7))
        shape('back_hunch', 0, (.75, .49, .51))
        shape('head', 0, (.42, .41, .40))
        shape('arm', 0, (.21, 1.03, .21), rotation=(-18, 0, -8))
        shape('leg', rotation=(0, 0, 4))
    elif role == '173':
        # Concrete peanut silhouette, broad belly and oversized rounded head.
        shape('torso', 0, (.64, .85, .49))
        shape('shoulders', 0, (.76, .38, .50))
        shape('head', 0, (.56, .59, .46))
        for side in ('left', 'right'):
            shape(side + '_arm', 0, (.20, .70, .24), rotation=(78, 0, 0))
    elif role == '939':
        # Low muscular hunter, tapered tail and a crocodilian open mouth.
        shape('body', 0, (.78, .70, 1.78))
        shape('belly', 0, (.54, .25, 1.49))
        shape('head', 0, (.64, .49, .76))
        shape('leg_front', rotation=(-16, 0, 0), size=(.23, .43, .28))
        shape('leg_rear', rotation=(19, 0, 0), size=(.26, .43, .27))
        shape('upper_jaw', rotation=(-9, 0, 0), size=(.53, .20, .54))
        shape('lower_jaw', rotation=(12, 0, 0), size=(.50, .17, .52))
        shape('tail', 0, (.24, .24, .95))
    elif role == '3114':
        # Narrower waist and tilted rib cage, splayed hands, elongated skull.
        shape('pelvis', size=(.39, .21, .22), rotation=(0, 0, -6))
        for prefix in ('rib_lower', 'rib_middle', 'rib_upper'):
            shape(prefix, rotation=(0, 9, -7))
        shape('skull', size=(.34, .43, .35))
        shape('jaw', size=(.23, .14, .18), rotation=(8, 0, 0))
        shape('arm', rotation=(7, 0, 0))
        shape('hand', size=(.09, .17, .065), offset=(0, -.025, -.045))
    else:
        raise ValueError(role)
    return builder
