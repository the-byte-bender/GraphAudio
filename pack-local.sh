#!/bin/bash

set -e

LOCAL_FEED="./local-packages"
PROJECTS=(
    "GraphAudio.Core"
    "GraphAudio.IO"
    "GraphAudio.Realtime"
    "GraphAudio.SteamAudio"
)

if [ ! -d "$LOCAL_FEED" ]; then
    mkdir -p "$LOCAL_FEED"
    echo -e "Created local package feed at $LOCAL_FEED"
fi

echo -e "\nPacking GraphAudio packages"

for project in "${PROJECTS[@]}"; do
    echo -e "\nPacking $project..."
    
    project_path="./$project/$project.csproj"
    
    if [ -f "$project_path" ]; then
        dotnet pack "$project_path" -c Debug -o "$LOCAL_FEED"
        echo -e "$project packed successfully"
    else
        echo -e "Project not found: $project_path"
        exit 1
    fi
done

echo -e "\nAll packages packed to $LOCAL_FEED"
