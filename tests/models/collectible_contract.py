"""Identity and physical-budget checks for the collectible gallery style."""
from scp_model_contract import load, validate, _world_aabb

# Budget remains within five primitives of each original room exhibit.
LIMITS = {'049':(12,17,3.1,.95), '079':(7,12,2,1),
          '096':(14,19,3,.8), '106':(11,16,1.85,1),
          '173':(11,16,2,.95), '939':(13,18,1.45,2.5),
          '3114':(18,23,2.5,.60)}


def run(role):
    old,cap,height,radius=LIMITS[role]
    issues,stats=validate('scp-'+role,min_visible=old,max_visible=cap,
        required_markers=('marker_pivot','marker_screen' if role=='079' else 'marker_head'),
        min_y=-.05,max_y=height,max_radius=radius,min_distinct_colors=3)
    blocks=[b for b in load('scp-'+role)['Blocks'] if b.get('Properties',{}).get('PrimitiveFlags')==2]
    def group(prefix): return [b for b in blocks if b['Name'].rsplit('_',1)[0]==prefix]
    def one(prefix):
        found=group(prefix)
        assert len(found)==1,(role,prefix,len(found))
        return found[0]
    def check(ok,message):
        if not ok: issues.append(message)
    if role=='049':
        check(len(group('lens'))==2,'plague mask needs two dark lenses')
        check(one('beak')['Position']['z']>one('mask_head')['Position']['z']+.3,'beak must project from mask')
        check(one('hat_brim')['Scale']['x']>one('mask_head')['Scale']['x']*1.5,'hat must have a wide brim')
    elif role=='079':
        check(len(group('pixel_eye'))==2,'CRT face needs two pixel eyes')
        check(one('screen')['Position']['z']>one('monitor')['Position']['z'],'screen faces the aisle')
        check(one('keyboard')['Position']['z']>one('screen')['Position']['z'],'keyboard projects toward user')
        check(one('screen')['Scale']['x']>one('screen')['Scale']['y'],'CRT screen is landscape')
    elif role=='096':
        check(len(group('shin'))==2 and len(group('upper_arm'))==2 and len(group('forearm'))==2,'gaunt figure needs four complete limbs')
        check(one('torso')['Scale']['y']>2*one('torso')['Scale']['x'],'torso must remain emaciated')
        check(all(h['Position']['y']>2 for h in group('hand')),'hands must frame the distressed face')
        check(one('mouth')['Position']['z']>one('head')['Position']['z'],'open mouth must face viewer')
    elif role=='106':
        check(one('head')['Position']['z']>one('back_hunch')['Position']['z']+.25,'head must jut forward of hunch')
        check(len(group('eye'))==2 and len(group('grin'))==1,'old man needs dark eyes and a grin')
        check(one('hand_right')['Position']['z']>one('head')['Position']['z']+.25,'reaching hand must project')
        check(_world_aabb(one('tar_shadow'))[1]>=-.001,'tar pool stays on deck')
    elif role=='173':
        check(len(group('foot'))==2 and len(group('arm'))==2,'concrete figure must keep paired supports and arms')
        check(one('head')['Scale']['x']>one('torso')['Scale']['x'],'head must read as heavy concrete')
        check(one('face')['Position']['z']>one('head')['Position']['z'],'painted face must be visible')
        check(len(group('eye'))==2 and len(group('paint_slash'))==1,'paint markings are required')
    elif role=='939':
        check(not any('eye' in b['Name'] for b in blocks),'939 must be eyeless')
        check(len(group('upper_leg'))==4 and len(group('lower_leg'))==4,'hunter needs four bent legs')
        check(one('body')['Scale']['z']>one('body')['Scale']['y']*2,'hunter must be long and low')
        check(one('upper_jaw')['Position']['y']>one('lower_jaw')['Position']['y']+.25,'maw must remain open')
        check(len(group('teeth_top'))==1 and len(group('teeth_bottom'))==1,'both tooth rows must read')
    else:
        check(len(group('rib'))==3 and len(group('spine'))==1,'skeleton needs an open rib cage and spine')
        check(len(group('eye_socket'))==2,'skull needs two eye sockets')
        check(one('jaw')['Position']['y']<one('skull')['Position']['y'],'jaw must sit below skull')
        check(len(group('leg'))==2 and len(group('forearm'))==2,'skeleton must keep complete limbs')
    if issues:
        raise AssertionError('SCP-'+role+': '+ '; '.join(issues))
    print('PASS SCP-'+role,stats)
