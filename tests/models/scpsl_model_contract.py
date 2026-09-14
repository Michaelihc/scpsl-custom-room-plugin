"""Physical limits and current-wiki identity checks, independent of model recipes."""
from scp_model_contract import load, validate

LIMITS={'049':(12,18,3.1,.95),'079':(7,14,2,1),'096':(14,22,3,.85),
        '106':(11,22,2,.8),'173':(11,22,2.4,1.1),'939':(13,28,2.6,1),
        '3114':(18,28,2.5,.6)}


def run(role):
    old,cap,height,radius=LIMITS[role]
    issues,stats=validate('scp-'+role,min_visible=old,max_visible=cap,
        required_markers=('marker_pivot','marker_screen' if role=='079' else 'marker_head'),
        min_y=-.05,max_y=height,max_radius=radius,min_distinct_colors=3)
    blocks=[p for p in load('scp-'+role)['Blocks'] if p.get('Properties',{}).get('PrimitiveFlags')==2]
    def group(name): return [p for p in blocks if p['Name'].rsplit('_',1)[0]==name]
    def one(name):
        found=group(name)
        assert len(found)==1,(role,name,len(found))
        return found[0]
    def check(ok,msg):
        if not ok: issues.append(msg)
    if role=='049':
        check(len(group('hood'))==1 and not any('hat_' in p['Name'] for p in blocks),'SL doctor wears a hood, not a brimmed hat')
        check(one('hood')['Scale']['x']>one('mask_head')['Scale']['x'],'hood must surround the mask')
        check(one('beak')['Position']['z']>one('mask_head')['Position']['z']+.15,'bird beak projects forward')
        check(len(group('robe_panel'))==2 and len(group('glove'))==2,'long robe and black gloved arms are required')
    elif role=='079':
        check(len(group('screen_x'))==2,'CRT must carry the white X')
        angles=[p['Rotation']['z'] for p in group('screen_x')]
        check(min(angles)<0<max(angles),'X strokes must cross')
        check(one('keyboard')['Position']['z']>one('screen')['Position']['z'],'keyboard sits in front of the CRT')
        check(not group('pixel_eye'),'remove invented cartoon screen face')
    elif role=='096':
        check(len(group('thigh'))==2 and len(group('shin'))==2,'long legs require knees')
        check(one('ribcage')['Scale']['x']>one('abdomen')['Scale']['x']*1.5,'rib cage must widen above emaciated waist')
        check(all(p['Position']['y']<.8 for p in group('hand')),'long arms hang below knees')
        check(one('head')['Scale']['x']<one('ribcage')['Scale']['x']*.6,'head must retain adult proportions')
        check(len(group('eye_hollow'))==2,'sunken eyes are required')
    elif role=='106':
        check(len(group('vest'))==2,'current model has a cropped open black vest')
        check(one('head')['Position']['z']>one('chest')['Position']['z']+.12,'bald head leans forward')
        check(len(group('eye'))==2 and len(group('grin'))==1,'pale eyes and exposed grin must read')
        check(one('abdomen')['Properties']['Color']!=group('vest')[0]['Properties']['Color'],'rotten exposed abdomen must differ from vest')
    elif role=='173':
        check(all(len(group(n))==1 for n in ('main_lobe','chest_lobe','side_lobe')),'Matthew needs its asymmetric fused concrete lobes')
        check(one('main_lobe')['Position']['x']!=one('side_lobe')['Position']['x'],'lobes must be asymmetric')
        check(len(group('thorn'))==2,'side spurs must remain visible')
        check(len(group('main_green'))==2 and len(group('chest_green'))==2,'main and chest markings must read green')
        check(not group('face'),'remove the legacy rectangular face plate')
    elif role=='939':
        check(len(group('thigh'))==2 and len(group('shin'))==2,'current 939 is a humanoid with two legs')
        check(len(group('upper_arm'))==2 and len(group('claw_hand'))==2 and len(group('finger_claw'))==4,'current 939 needs arms and hands')
        check(not group('tail') and not any('eye' in p['Name'] for p in blocks),'current 939 has neither a dog tail nor eyes')
        check(len(group('head_spine'))==2 and len(group('fang_row'))==2,'head spines and the toothed maw are required')
        check(one('chest')['Position']['y']>one('hips')['Position']['y']+.4,'torso must stand upright')
    else:
        check(len(group('rib'))==6 and len(group('sternum'))==1,'skeleton needs an open rib cage around a sternum')
        check(one('skull')['Scale']['x']<.35,'skull uses adult proportions')
        check(len(group('eye_socket'))==2 and len(group('pelvis_wing'))==2,'skull sockets and anatomical pelvis are required')
        check(len(group('thigh'))==2 and len(group('shin'))==2,'skeleton needs articulated legs')
    if issues: raise AssertionError('SCP-'+role+': '+'; '.join(issues))
    print('PASS SCP-'+role,stats)
