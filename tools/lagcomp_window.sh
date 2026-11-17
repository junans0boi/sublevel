#!/usr/bin/env bash
# usage: tools/lagcomp_window.sh <tag> <testFilter>   (env LAGCOMP_* passes through)
# One measured PlayMode run in an exclusive window: ps/uptime before and after, audio off, raw logs kept in Logs/lag/<tag>*
# (Logs/ is git-ignored but survives a reboot; summaries go to docs/research/).
cd "$(dirname "$0")/.."
tag=$1; filter=$2; out=Logs/lag; mkdir -p $out
snap() { echo "== $1 $tag $(date +%T) $(uptime)" >> $out/ps.log; ps -axo pid,pcpu,command | grep -iE "Unity.app/Contents/MacOS/Unity |Sublevel" | grep -v grep | grep -vE "Unity Hub|Licensing|claude" | cut -c1-140 >> $out/ps.log; }
sed -i '' 's/m_DisableAudio: 0/m_DisableAudio: 1/' ProjectSettings/AudioManager.asset
snap before
rm -rf Logs/net-multi Logs/test-playmode.xml
tools/unity.sh test playmode "$filter" > /dev/null 2>&1
cp Logs/test-playmode.xml $out/$tag.xml; cp Logs/test-playmode.log $out/$tag.log; cp -R Logs/net-multi $out/$tag-net 2>/dev/null
snap after
git restore -- ProjectSettings
