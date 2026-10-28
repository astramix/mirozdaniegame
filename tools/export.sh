#!/usr/bin/env bash
# Сборка игры (этап F): Linux и Windows. Шаблоны экспорта Godot 4.4.1 .NET ставятся при первом запуске (~1,1 ГБ, берём только нужные).
set -euo pipefail
cd "$(dirname "$0")/.."
TPL="$HOME/.local/share/godot/export_templates/4.4.1.stable.mono"
if [ ! -f "$TPL/linux_release.x86_64" ]; then
  tmp=$(mktemp -d)
  curl -sSL -o "$tmp/tpl.tpz" https://github.com/godotengine/godot/releases/download/4.4.1-stable/Godot_v4.4.1-stable_mono_export_templates.tpz
  mkdir -p "$TPL"
  unzip -o -q "$tmp/tpl.tpz" 'templates/linux_*x86_64' 'templates/windows_*x86_64*' 'templates/macos.zip' 'templates/version.txt' -d "$tmp"
  mv "$tmp"/templates/* "$TPL"/
  rm -rf "$tmp"
fi
for preset in "${@:-Linux Windows}"; do
  for p in $preset; do
    case "$p" in
      Linux) out=build/Linux/mirozdanie.x86_64 ;;
      Windows) out=build/Windows/mirozdanie.exe ;;
      macOS) out=build/macOS/mirozdanie.zip ;;
    esac
    mkdir -p "$(dirname "$out")"
    godot --headless --path game --export-release "$p" "../$out"
    echo "Собрано: $out"
  done
done
# Дымовой тест собранной Linux-версии.
if [ -x build/Linux/mirozdanie.x86_64 ]; then ./build/Linux/mirozdanie.x86_64 --headless -- --smoke | grep "SMOKE OK"; fi
