#!/usr/bin/env python3
# Writes Assets/_Project/Scripts/Core/World/IslandData.cs from the C2 design numbers (docs/research/map_options/option_C2_river_mountain.html,
# docs/bible/data/{towns,roads,terrain}.json). Re-run after editing the tables below; the output is the single source for IslandSpec.CreateDefault().
import re, sys, os
OUT = os.path.join(os.path.dirname(__file__), '..', '..', 'Assets/_Project/Scripts/Core/World/IslandData.cs')
KIND = {'상가':'Shop','주택':'House','노점':'Stall','빌라':'Villa','아파트':'Apartment','막사':'Barracks','학교':'School','감시탑':'WatchTower','공장':'FactoryHall','창고':'Warehouse','헛간':'Shed','시장':'Market'}
# id, name, tier, region, x, z, rot, W, H, seed, composition
TOWNS = [
 ('C','청람읍',1,1,960,1000,0,440,300,11,'상가 36·주택 21·노점 8·빌라 6·아파트 4·막사 4·학교 3·감시탑 2·공장 2·창고 2·헛간 1·시장 1'),
 ('L1','새재',2,4,1530,1100,8,330,240,21,'주택 20·상가 16·노점 8·아파트 4·시장 1·헛간 1'),
 ('L2','해옥포',2,2,640,1610,-14,300,230,22,'주택 16·상가 16·창고 5·헛간 1'),
 ('L3','광석리',2,5,1900,1500,10,320,240,23,'주택 8·빌라 8·상가 8·공장 4·막사 4·감시탑 3·창고 3·헛간 1'),
 ('L4','북령읍',2,0,880,570,-6,330,210,24,'주택 17·상가 17·빌라 8'),
 ('M1','학전',3,1,530,880,5,190,130,31,'상가 7·주택 6·학교 3'),
 ('M2','논실',3,4,1520,1320,-8,200,140,32,'주택 12·상가 8·헛간 1'),
 ('M3','마루재',3,4,1840,1085,0,190,130,33,'주택 14·상가 7'),
 ('M4','채석리',3,3,1990,670,12,200,140,34,'주택 8·상가 8·창고 2·헛간 1'),
 ('M5','남포',3,5,1495,1790,-5,220,140,35,'주택 9·상가 8·창고 2·헛간 1'),
 ('M6','북해리',3,0,1010,180,0,200,140,36,'주택 11·상가 7·헛간 1'),
 ('H1','솔밭',4,1,390,1130,20,160,50,41,'주택 6'),
 ('H3','샘골',4,0,1470,560,0,160,50,42,'주택 6'),
 ('H4','양지',4,3,2100,1040,-10,170,50,43,'주택 6'),
 ('H5','달뫼',4,1,880,1190,30,100,60,44,'주택 8'),
 ('H6','하동',4,4,1390,1560,0,150,50,45,'주택 4'),
 ('H7','갯말',4,2,850,1790,0,150,50,46,'주택 6'),
]
NODES = [('P1','북령고개',1010,345),('P3','서령재',640,1245),('P4','남령재',1698,1445),('P5','샘골재',1400,355),('J5','교차로',1000,1745)]
# a, b, class, via points, switchback
EDGES = [
 ('C','L1','H',[(1150,990),(1380,1010)],0),('L1','M2','H',[(1530,1240)],0),('M2','P4','H',[(1690,1340)],0),('P4','L3','H',[(1800,1480)],1),
 ('L3','M5','H',[(1760,1700),(1600,1790)],0),('M5','J5','H',[(1330,1830),(1150,1790)],0),('J5','L2','H',[(880,1690)],0),
 ('H1','P3','D',[(500,1190)],0),('H1','M1','D',[(430,1000)],0),('M1','C','P',[(700,900),(850,960)],0),('C','H5','P',[(950,1100)],0),
 ('H5','P3','P',[(760,1220)],1),('P3','L2','P',[(620,1420),(640,1520)],1),
 ('M1','L4','P',[(640,700)],0),('C','L4','P',[(960,800)],0),('L4','P1','P',[(950,460)],0),('P1','M6','P',[(1040,250)],0),('M6','P5','P',[(1230,215),(1330,280)],1),('P5','H3','P',[(1430,450)],1),
 ('L4','H3','P',[(1060,610),(1170,630),(1300,590)],0),('H3','L1','P',[(1400,800),(1440,950)],0),('H3','M4','P',[(1620,600),(1800,640)],0),('M4','H4','P',[(2060,850)],0),
 ('L1','M3','P',[(1630,1100)],1),('M3','H4','P',[(1900,1110),(2010,1090)],1),('H4','L3','P',[(2060,1290)],0),
 ('M2','H6','D',[(1470,1440)],0),('H6','M5','D',[(1450,1650)],0),('H7','J5','D',[(930,1760)],0)]
RIDGES = [('백운 능선',[(250,640),(650,440),(1000,340),(1450,360),(1900,330),(2100,540)],165,150),
          ('동령 능선',[(1560,700),(1720,950),(1740,1300),(1640,1650)],105,120),
          ('서령 능선',[(280,1300),(620,1230),(900,1330),(1050,1500)],92,115)]
PEAKS = [('백운봉',790,410,110,120),('서령봉',470,540,55,110),('동령봉',2000,430,75,150),('솔뫼봉',1690,800,45,90),('망루봉',650,1040,85,100),('남석 구릉',1990,1320,55,90)]
PASSES = [('P1','북령고개',1010,345,1),('P2','동령재',1735,1090,1),('P3','서령재',640,1245,1),('P4','남령재',1698,1445,1),('P5','샘골재',1400,355,1),
          ('S4','서령 북안부',520,505,0),('S2','서령 안부',885,1325,0),('S3','솔뫼 안부',1656,850,0)]
QUARRIES = [('동령 채석장',2090,600,105),('남석 채석장',1990,1330,90)]
RIVERS = [('청람강',[(1170,150),(1120,360),(1190,560),(1160,780),(1260,990),(1230,1180),(1330,1360),(1270,1560),(1350,1720),(1310,1910)],8,30,1.2,14,-2,3,.85,.3),
          ('동천',[(1700,1250),(1650,1360),(1520,1450),(1410,1440),(1318,1400)],5,9,1.0,30,0,2,.35,.35)]
# id, name, kind, seeds, west(1)/east(0) side flag, elevators, rocks/ha, trees/ha, min lots, convergence points
REGIONS = [('R0','북령 산악','Mountain',[(700,400),(1250,350)],0,3,45,40),('R1','청람읍권','Town',[(700,950)],1,4,8,25),('R2','서남 포구','Factory',[(700,1600)],1,3,6,15),
           ('R3','동령 고원','Mountain',[(1950,700)],0,3,40,25),('R4','동천 평야','Town',[(1550,1200)],0,4,6,20),('R5','남동 해안','Factory',[(1900,1500)],0,3,8,12)]
RELIEF = 0.75
ELEV = [('L4','e',.5,0),('L4','w',.5,0),('M6','w',.5,0),('C','w',.35,1),('C','n',.55,1),('M1','n',.5,1),('H5','e',.5,1),('L2','n',.5,2),('L2','e',.5,2),('H7','e',.5,2),
        ('M4','w',.5,3),('M4','e',.5,3),('H4','w',.5,3),('L1','w',.5,4),('L1','n',.5,4),('M2','n',.5,4),('M3','s',.5,4),('L3','n',.5,5),('L3','s',.5,5),('M5','e',.5,5)]
f = lambda v: ('%s' % (round(v, 3))).rstrip('0').rstrip('.') + 'f' if isinstance(v, float) else '%sf' % v
ENDS = {('L4','P1'): (-1, 0), ('L1','M2'): (-1, -1), ('M2','P4'): (1, 0), ('H6','M5'): (1, 1)}   # (a,b): (endA, endB) -1 west / +1 east avenue end, 0 auto
def pts(p): return 'new[] { ' + ', '.join('%s, %s' % (f(float(x)), f(float(y))) for x, y in p) + ' }'
def comp(s): return ','.join('%s:%s' % (KIND[m.group(1)], m.group(2)) for m in re.finditer(r'([가-힣]+) (\d+)', s))
o = []
w = o.append
w('// <auto-generated> tools/map/gen_island_data.py from the C2 design tables (docs/research/map_options/option_C2_river_mountain.html). Edit the script, not this file.')
w('namespace Game.Core')
w('{')
w('    public static partial class IslandData')
w('    {')
w('        public static IslandSpec Create() => new IslandSpec')
w('        {')
w('            Regions = new[]')
w('            {')
for i, (rid, n, k, seeds, west, el, rocks, trees) in enumerate(REGIONS):
    w('                new IslandRegion { Id = "%s", Name = "%s", Kind = "%s", Seeds = %s, West = %d, Elevators = %d, RocksPerHectare = %sf, TreesPerHectare = %sf },' % (rid, n, k, pts(seeds), west, el, rocks, trees))
w('            },')
w('            Towns = new[]')
w('            {')
for (i, n, t, r, x, z, rot, W, H, seed, c) in TOWNS:
    w('                new IslandTown { Id = "%s", Name = "%s", Tier = %d, Region = %d, X = %sf, Z = %sf, RotDeg = %sf, Width = %sf, Depth = %sf, Seed = %d, Composition = "%s" },' % (i, n, t, r, x, z, rot, W, H, seed, comp(c)))
w('            },')
w('            Nodes = new[]')
w('            {')
for i, n, x, z in NODES:
    w('                new IslandNode { Id = "%s", Name = "%s", X = %sf, Z = %sf },' % (i, n, x, z))
w('            },')
w('            Roads = new[]')
w('            {')
for a, b, c, via, sw in EDGES:
    ea, eb = ENDS.get((a, b), (0, 0))
    w('                new IslandRoad { A = "%s", B = "%s", Class = "%s", Via = %s, Switchback = %s, EndA = %d, EndB = %d },' % (a, b, c, pts(via), 'true' if sw else 'false', ea, eb))
w('            },')
w('            Ridges = new[]')
w('            {')
for n, p, amp, wd in RIDGES:
    w('                new IslandRidge { Name = "%s", Points = %s, Amp = %sf, Width = %sf },' % (n, pts(p), amp, wd))
w('            },')
w('            Peaks = new[]')
w('            {')
for n, x, z, h, s in PEAKS:
    w('                new IslandPeak { Name = "%s", X = %sf, Z = %sf, H = %sf, S = %sf },' % (n, x, z, h, s))
w('            },')
w('            Passes = new[]')
w('            {')
for i, n, x, z, r in PASSES:
    w('                new IslandPass { Id = "%s", Name = "%s", X = %sf, Z = %sf, Road = %s },' % (i, n, x, z, 'true' if r else 'false'))
w('            },')
w('            Quarries = new[]')
w('            {')
for n, x, z, r in QUARRIES:
    w('                new IslandQuarry { Name = "%s", X = %sf, Z = %sf, Radius = %sf },' % (n, x, z, r))
w('            },')
w('            Rivers = new[]')
w('            {')
for n, p, h0, h1, hp, b0, b1, bp, s0, s1 in RIVERS:
    w('                new IslandRiver { Name = "%s", Points = %s, Hw0 = %sf, Hw1 = %sf, HwPow = %sf, B0 = %sf, B1 = %sf, BedPow = %sf, Sl0 = %sf, Sl1 = %sf },' % (n, pts(p), h0, h1, hp, b0, b1, bp, s0, s1))
w('            },')
w('            Stations = new[]')
w('            {')
for t, side, fr, r in ELEV:
    w('                new IslandStation { Town = "%s", Side = "%s", Frac = %sf, Region = %d },' % (t, side, fr, r))
w('            },')
w('            ReliefScale = %sf,' % RELIEF)
w('        };')
w('    }')
w('}')
open(OUT, 'w').write('\n'.join(o) + '\n')
print('wrote', OUT)
