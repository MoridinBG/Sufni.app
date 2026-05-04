#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/setup-ios-personal-signing.sh --bundle-id <id> --team-id <team-id> [options]
  scripts/setup-ios-personal-signing.sh <bundle-id> <team-id> [options]

Configures the iOS head for personal device development:
  - writes CFBundleIdentifier in Sufni.App.iOS/Info.plist
  - changes the iOS csproj to use generic Apple Development signing
  - asks Xcode to create/download an automatic development provisioning profile
  - verifies with a dotnet iOS device build unless --skip-build-check is used

Options:
  --bundle-id <id>       iOS bundle id, for example com.example.Sufni.App
  --team-id <team-id>    Apple Developer Team ID, for example ABCDE12345
  --signing-key <name>   Codesign key name. Default: Apple Development
  --configuration <cfg>  Build configuration used for provisioning. Default: Debug
  --tfm <tfm>            Target framework. Default: net10.0-ios
  --rid <rid>            Runtime identifier. Default: ios-arm64
  --skip-provision       Only edit project files; do not call xcodebuild
  --skip-build-check     Do not run the final dotnet device build
  --dry-run              Print intended work without changing files
  -h, --help             Show this help

Prerequisites:
  - Xcode is signed in to the Apple account for the supplied team
  - the physical iOS device is registered for that team
  - an Apple Development certificate/private key is installed in Keychain
USAGE
}

fail() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

info() {
  printf '%s\n' "$*"
}

quote_cmd() {
  local first=1
  for arg in "$@"; do
    if [[ $first -eq 0 ]]; then
      printf ' '
    fi
    first=0
    printf '%q' "$arg"
  done
  printf '\n'
}

run() {
  printf '+ '
  quote_cmd "$@"
  if [[ "$DRY_RUN" == true ]]; then
    return 0
  fi
  "$@"
}

require_tool() {
  command -v "$1" >/dev/null 2>&1 || fail "required tool not found on PATH: $1"
}

validate_bundle_id() {
  local value="$1"
  [[ "$value" =~ ^[A-Za-z0-9][A-Za-z0-9-]*(\.[A-Za-z0-9][A-Za-z0-9-]*)+$ ]] ||
    fail "invalid bundle id: $value"
}

validate_team_id() {
  local value="$1"
  [[ "$value" =~ ^[A-Z0-9]{10}$ ]] ||
    fail "invalid Apple team id: $value"
}

set_xml_property() {
  local file="$1"
  local name="$2"
  local value="$3"

  if [[ "$DRY_RUN" == true ]]; then
    info "would set <$name>$value</$name> in $file"
    return 0
  fi

  XML_PROPERTY_NAME="$name" XML_PROPERTY_VALUE="$value" perl -0pi -e '
    my $name = $ENV{"XML_PROPERTY_NAME"};
    my $value = $ENV{"XML_PROPERTY_VALUE"};
    if (s{<\Q$name\E>.*?</\Q$name\E>}{<$name>$value</$name>}s) {
      exit 0;
    }
    s{(</PropertyGroup>)}{    <$name>$value</$name>\n$1} or die "No PropertyGroup found\n";
  ' "$file"
}

profile_search_dirs() {
  printf '%s\n' \
    "$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles" \
    "$HOME/Library/MobileDevice/Provisioning Profiles"
}

find_matching_profile() {
  local app_id="${TEAM_ID}.${BUNDLE_ID}"
  local dir
  local file

  while IFS= read -r dir; do
    [[ -d "$dir" ]] || continue
    while IFS= read -r -d '' file; do
      if strings -a "$file" | grep -Fq "<string>${app_id}</string>"; then
        printf '%s\n' "$file"
        return 0
      fi
    done < <(find "$dir" -maxdepth 1 -type f \( -name '*.mobileprovision' -o -name '*.provisionprofile' \) -print0)
  done < <(profile_search_dirs)

  return 1
}

ensure_generated_xcode_project() {
  if [[ -d "$GENERATED_XCODE_PROJECT" ]]; then
    return 0
  fi

  info "Generating .NET iOS build metadata..."
  if [[ "$DRY_RUN" == true ]]; then
    info "would run dotnet build to create $GENERATED_XCODE_PROJECT"
    return 0
  fi

  local log_file
  log_file="$(mktemp "${TMPDIR:-/tmp}/sufni-ios-signing-dotnet-build.XXXXXX.log")"

  set +e
  dotnet build "$IOS_PROJECT" \
    -c "$CONFIGURATION" \
    -f "$TFM" \
    -r "$RID" \
    -p:RuntimeIdentifier="$RID" \
    -p:CodesignKey="$SIGNING_KEY" >"$log_file" 2>&1
  local build_status=$?
  set -e

  if [[ $build_status -ne 0 && ! -d "$GENERATED_XCODE_PROJECT" ]]; then
    cat "$log_file" >&2
    fail "dotnet build failed before creating $GENERATED_XCODE_PROJECT"
  fi

  if [[ $build_status -ne 0 ]]; then
    info "Initial dotnet build did not complete, but the generated Xcode project exists; continuing with provisioning."
  fi
}

create_or_refresh_profile() {
  local existing_profile
  existing_profile="$(find_matching_profile || true)"
  if [[ -n "$existing_profile" ]]; then
    info "Found matching provisioning profile: $existing_profile"
    return 0
  fi

  ensure_generated_xcode_project

  info "Requesting automatic Xcode provisioning for ${TEAM_ID}.${BUNDLE_ID}..."
  if [[ "$DRY_RUN" == true ]]; then
    info "would run xcodebuild -allowProvisioningUpdates for $BUNDLE_ID and team $TEAM_ID"
    return 0
  fi

  local log_file
  log_file="$(mktemp "${TMPDIR:-/tmp}/sufni-ios-signing-xcodebuild.XXXXXX.log")"

  set +e
  xcodebuild \
    -project "$GENERATED_XCODE_PROJECT" \
    -scheme Sufni.App.iOS \
    -configuration "$CONFIGURATION" \
    -sdk iphoneos \
    -destination 'generic/platform=iOS' \
    -allowProvisioningUpdates \
    DEVELOPMENT_TEAM="$TEAM_ID" \
    PRODUCT_BUNDLE_IDENTIFIER="$BUNDLE_ID" \
    CODE_SIGN_STYLE=Automatic \
    CODE_SIGN_IDENTITY="$SIGNING_KEY" \
    build >"$log_file" 2>&1
  local xcode_status=$?
  set -e

  existing_profile="$(find_matching_profile || true)"
  if [[ -n "$existing_profile" ]]; then
    info "Provisioning profile is available: $existing_profile"
    return 0
  fi

  cat "$log_file" >&2
  fail "xcodebuild did not create a provisioning profile for ${TEAM_ID}.${BUNDLE_ID}"
}

verify_codesign_identity() {
  if [[ "$DRY_RUN" == true ]]; then
    info "would check for an Apple Development code signing identity"
    return 0
  fi

  security find-identity -v -p codesigning | grep -F "$SIGNING_KEY" >/dev/null ||
    fail "no '$SIGNING_KEY' code signing identity found in Keychain"
}

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IOS_DIR="$ROOT_DIR/Sufni.App/Sufni.App.iOS"
IOS_PROJECT="$IOS_DIR/Sufni.App.iOS.csproj"
INFO_PLIST="$IOS_DIR/Info.plist"
GENERATED_XCODE_PROJECT="$IOS_DIR/obj/Xcode/0/Sufni.App.iOS.xcodeproj"

BUNDLE_ID=""
TEAM_ID=""
SIGNING_KEY="Apple Development"
CONFIGURATION="Debug"
TFM="net10.0-ios"
RID="ios-arm64"
SKIP_PROVISION=false
SKIP_BUILD_CHECK=false
DRY_RUN=false

POSITIONAL=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --bundle-id)
      [[ $# -ge 2 ]] || fail "--bundle-id requires a value"
      BUNDLE_ID="$2"
      shift 2
      ;;
    --team-id)
      [[ $# -ge 2 ]] || fail "--team-id requires a value"
      TEAM_ID="$2"
      shift 2
      ;;
    --signing-key)
      [[ $# -ge 2 ]] || fail "--signing-key requires a value"
      SIGNING_KEY="$2"
      shift 2
      ;;
    --configuration)
      [[ $# -ge 2 ]] || fail "--configuration requires a value"
      CONFIGURATION="$2"
      shift 2
      ;;
    --tfm)
      [[ $# -ge 2 ]] || fail "--tfm requires a value"
      TFM="$2"
      shift 2
      ;;
    --rid)
      [[ $# -ge 2 ]] || fail "--rid requires a value"
      RID="$2"
      shift 2
      ;;
    --skip-provision)
      SKIP_PROVISION=true
      shift
      ;;
    --skip-build-check)
      SKIP_BUILD_CHECK=true
      shift
      ;;
    --dry-run)
      DRY_RUN=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    --)
      shift
      while [[ $# -gt 0 ]]; do
        POSITIONAL+=("$1")
        shift
      done
      ;;
    -*)
      fail "unknown option: $1"
      ;;
    *)
      POSITIONAL+=("$1")
      shift
      ;;
  esac
done

if [[ ${#POSITIONAL[@]} -gt 0 ]]; then
  [[ -z "$BUNDLE_ID" ]] || fail "bundle id was supplied twice"
  BUNDLE_ID="${POSITIONAL[0]}"
fi

if [[ ${#POSITIONAL[@]} -gt 1 ]]; then
  [[ -z "$TEAM_ID" ]] || fail "team id was supplied twice"
  TEAM_ID="${POSITIONAL[1]}"
fi

[[ ${#POSITIONAL[@]} -le 2 ]] || fail "too many positional arguments"
[[ -n "$BUNDLE_ID" ]] || fail "missing --bundle-id"
[[ -n "$TEAM_ID" ]] || fail "missing --team-id"

validate_bundle_id "$BUNDLE_ID"
validate_team_id "$TEAM_ID"

[[ -f "$IOS_PROJECT" ]] || fail "iOS project not found: $IOS_PROJECT"
[[ -f "$INFO_PLIST" ]] || fail "Info.plist not found: $INFO_PLIST"

require_tool dotnet
require_tool xcodebuild
require_tool security
require_tool strings
require_tool grep
require_tool find
require_tool perl
[[ -x /usr/libexec/PlistBuddy ]] || fail "required tool not found: /usr/libexec/PlistBuddy"

verify_codesign_identity

info "Configuring iOS bundle id: $BUNDLE_ID"
run /usr/libexec/PlistBuddy -c "Set :CFBundleIdentifier $BUNDLE_ID" "$INFO_PLIST"

info "Configuring generic iOS development signing in $IOS_PROJECT"
set_xml_property "$IOS_PROJECT" "CodesignKey" "$SIGNING_KEY"
set_xml_property "$IOS_PROJECT" "CodesignProvision" "Automatic"

if [[ "$SKIP_PROVISION" == false ]]; then
  create_or_refresh_profile
fi

if [[ "$SKIP_BUILD_CHECK" == false ]]; then
  info "Verifying iOS device build..."
  run dotnet build "$IOS_PROJECT" \
    -c "$CONFIGURATION" \
    -f "$TFM" \
    -r "$RID" \
    -p:RuntimeIdentifier="$RID" \
    -p:CodesignKey="$SIGNING_KEY"
fi

info "iOS personal signing setup complete."
