#!/bin/sh
# NativeHttpChannel 을 데스크톱 JDK(8 이상)로 컴파일해 로컬 서버와 왕복한다. Android SDK 는 필요 없다
set -e
here=$(cd "$(dirname "$0")" && pwd)
src="$here/../../Runtime/Plugins/Android/GeunedaHttp.androidlib/src/main/java/com/geuneda/http/NativeHttpChannel.java"
out=$(mktemp -d)
trap 'rm -rf "$out"' EXIT
javac --release 8 -Xlint:-options -d "$out" "$src" "$here/NativeHttpChannelTest.java"
java -cp "$out" com.geuneda.http.NativeHttpChannelTest
