; Inno Setup script for FreeWilly (DD14).
;
; Build, from the repository root:
;   build\build-installer.cmd
; or by hand:
;   dotnet publish src\FreeWilly.Tray -c Release
;   "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" build\installer.iss
; Output: dist\FreeWilly-Setup.exe
;
; Every relative path below resolves against THIS file's directory (build\), so the ones pointing
; at the repository root are "..\"-relative.

#define MyAppName "FreeWilly"
#define MyAppPublisher "FreeWilly contributors"

; Still the old repository. Renaming it moves every published URL at once and is not this file's to
; do — DD59 waits on the GitHub rename itself.
#define MyAppUrl "https://github.com/alegauss/freewilly"

#define MyAppExeName "FreeWilly.exe"
#define MyPublishDir "..\src\FreeWilly.Tray\bin\Release\net10.0-windows\win-x64\publish"

; DD141's forwarder, published separately because it is the one thing here that cannot be the tray's
; binary: a command a shell waits for has to be console-subsystem, and a tray application is not.
#define MyShimDir "..\src\FreeWilly.Shim\bin\Release\net10.0\win-x64\publish"

; Read straight off the published .exe, which got it from <Version> in Directory.Build.props. There
; is no second version to bump here, and a PackagingTests case holds that string to "x.y.z" with no
; commit suffix — Add/Remove Programs shows this verbatim. Requires the publish to have run first.
#define MyAppVersion GetStringFileInfo(MyPublishDir + "\" + MyAppExeName, PRODUCT_VERSION)

[Setup]
; Inno identifies a product by AppId and by nothing else, so from the first release onward THIS
; STRING NEVER CHANGES. Changing it is the one move that cannot be undone from here: the new setup
; would not see the old install, so a machine would carry two entries in Add/Remove Programs, two
; Run values and two roots — and the old uninstaller, run afterwards, offers to delete the engine
; root the new install is now using. It is also what makes an upgrade land where the previous
; install did, because Inno records the install directory against it.
;
; DD57 kept the previous id for exactly that reason and left the old product name visible inside it.
; DD86 changed it once, and could: nothing has been released, so there is no copy on any machine
; whose identity this is. That window closes with the first published tag.
AppId={{6B0E4D2A-9C77-4A31-8F5E-FREEWILLY0001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}
VersionInfoVersion={#MyAppVersion}

; The whole point of the per-user install. `lowest` means no administrator prompt for the
; application: the audience is developers on managed corporate laptops, and a UAC dialog at install
; time is where a large share of them stop. The engine's WSL2 feature may still need elevation of its
; own, which is why the preflight below states that before anything is downloaded rather than a
; dialog appearing halfway through a provision.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; {app} is deliberately the same directory EnginePaths calls Root, so everything this tool owns —
; the executable, the downloads, the distribution, the docker CLI — is under one folder a person can
; find, and the uninstall has one place to ask about.
;
; Inno records the directory against the AppId, so an upgrade reuses whatever the previous install
; chose and a fresh one gets this. DD57 weighed changing the AppId and did not: see the note over it.
DefaultDirName={localappdata}\FreeWilly
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes

; WSL2 needs Windows 10 2004. Saying so here is cheaper than a preflight on a machine that was never
; going to work, and MinVersion is the one check Inno can make before anything is written.
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
SetupIconFile=..\src\FreeWilly.Tray\FreeWilly.ico
OutputDir=..\dist
OutputBaseFilename=FreeWilly-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE

; DD21. Measured: launching the tray creates a NotifyIconSettings entry with IsPromoted absent, so
; the icon registers and Windows 11 files it into the overflow — the documented default for an icon
; the shell has not seen before. This tool does not promote itself out of it, so the install has to
; say where the icon went rather than leave somebody hunting for a state indicator that was promised
; as a glance. Shown as its own page, and skipped automatically in a silent install.
InfoAfterFile=after-install.txt

; The tray may be running from a previous install. Restart Manager closes it without forcing a
; reboot; RestartApplications=no because nothing here needs the machine restarted.
;
; DD121: this covers the uninstall too, and it is the backstop rather than the plan. The uninstall
; asks the product to close itself first — a terminated tray leaves its icon in the notification area
; until something hovers it — and only what a graceful exit missed reaches Restart Manager.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start {#MyAppName} with Windows"; GroupDescription: "Startup:"

; DD119. Ticked by default, because an install that leaves the engine out is an install of nothing:
; `docker` is not a command, Start engine has no distribution to boot, and the only thing that
; changes either is a verb the wizard never named. Unticking leaves exactly that install, on purpose
; — a quarter of a gigabyte over somebody's tethered connection is theirs to decline, and
; `FreeWilly.exe --provision` does the same work later.
;
; Downloading is not starting. The engine is still not running when Setup closes, and no service is
; registered: a resident background service is a stated non-goal, and an installer that leaves a
; container engine running is the weight this project is an answer to.
Name: "engine"; Description: "Download and install the container engine (about 250 MB)"; \
    GroupDescription: "Container engine:"

Name: "pathentry"; Description: "Put docker and freewilly on my PATH"; GroupDescription: "Command line:"

[Files]
; The product itself, and DD14's whole shape: one .exe to publish, to sign, to install and to hand
; somebody. DD141 adds the second and only other published binary — the docker forwarder below —
; and it is an exception rather than a drift, for a reason no design choice can remove: a command a
; shell waits for must be console-subsystem, and this file is a tray application.
Source: "{#MyPublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; The same file again, and it is the same file: MergeDuplicateFiles is on by default, so two entries
; naming one source are stored once. Measured on a 20 MB incompressible payload — 23,069,659 bytes
; with both entries against 23,069,662 with one — which is what makes DD130 affordable, since the
; alternative reading of "run the check first" is a second executable to publish and sign.
;
; dontcopy is what ExtractTemporaryFile requires and is also the whole of this entry's behaviour:
; nothing is installed by it. The preflight page below puts this copy in {tmp} and runs it there,
; before Setup has written anything to {app} — so a machine that cannot host an engine is told so
; while there is still nothing on it to undo.
Source: "{#MyPublishDir}\{#MyAppExeName}"; Flags: dontcopy

; DD24. The agent surface is reached as `freewilly read ...`, which is the literal string an
; allowlist entry matches - `Bash(freewilly read:*)`. The .exe lives in {app} and only {app}\bin is
; on PATH, so without this the one command the whole read/do split exists to make grantable does not
; resolve at all. A forwarder rather than a second PATH entry: one name on PATH, one thing to remove.
Source: "freewilly.cmd"; DestDir: "{app}\bin"; Flags: ignoreversion; Tasks: pathentry

; DD141. The `docker` a shell actually runs: the vendor's CLI, plus the one sentence its failure
; could not know. A stopped engine reaches an agent as docker's own connection error — written for a
; world where the daemon could be anyone's — and the verb that fixes it is known right where that
; error is printed.
;
; A second published binary, and the only one. It has to be, and the reason is the subsystem: a
; command a shell waits for must be subsystem 3, FreeWilly.exe is a tray application and therefore
; subsystem 2, and Windows hands the prompt back before a windowed process has printed anything.
; A copy of the one .exe under this name would have broken every script to improve one message.
;
; Gated on the same checkbox as the PATH entry, and that is the rule rather than a convenience: this
; is what makes this install the owner of the `docker` command, so a user who declined that keeps
; whatever docker they already had. The vendor's CLI is placed by the provision one directory across
; in {app}\cli, because PATHEXT resolves .EXE before everything else and the two cannot share a
; directory.
Source: "{#MyShimDir}\docker.exe"; DestDir: "{app}\bin"; Flags: ignoreversion; Tasks: pathentry

; DD32. How the surface is found, shipped beside it: a skill naming the verbs and the one rule, and
; the allowlist line that makes the read/do split pay. Laid down in {app}\agent and nowhere else -
; this install never touches a user's .claude directory, because an agent configuration is exactly
; the file where a tool writing without asking would be least forgivable. The after-install page
; prints the two commands and the user decides.
Source: "agent\SKILL.md"; DestDir: "{app}\agent"; Flags: ignoreversion
Source: "agent\settings-snippet.json"; DestDir: "{app}\agent"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Autostart, per-user and off unless it was asked for. --tray is load-bearing: a bare launch opens
; the window (DD80), and a window in the face at every logon is exactly the regression that change
; could otherwise cause. This is the only caller that wants the tray on its own.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "{#MyAppName}"; \
    ValueData: """{app}\{#MyAppExeName}"" --tray"; \
    Flags: uninsdeletevalue; Tasks: startupicon

; The engine's own Run value, which this installer never writes — `freewilly --autostart on` does,
; and only if the user asks (DD97). It is named here so the uninstaller takes it: two settings mean
; two values, and leaving one behind is an entry pointing at an executable that has been deleted.
;
; ValueType: none is what makes that "delete on uninstall, touch nothing on install". Writing it
; here instead would turn the engine autostart ON for everyone, and off-by-default is not a
; preference in this product — it is the whole complaint about Docker Desktop.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: none; ValueName: "FreeWilly Engine"; \
    Flags: uninsdeletevalue

; EnginePaths says putting the CLI folder on PATH is the installer's job, and this is it. HKCU, so
; no elevation; expandsz, because that is what Windows keeps Path as and rewriting it as a plain
; string would flatten every %VAR% already in it.
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; \
    ValueData: "{olddata};{app}\bin"; Tasks: pathentry; Check: PathEntryMissing

; DOCKER_CONFIG, which is what makes `docker compose` a subcommand in the user's own shell (DD124).
; The plugins DD73 and DD74 place sit in {app}\cli-plugins, and the CLI looks for a plugin in
; $DOCKER_CONFIG\cli-plugins and nowhere else — so without this, an `ant` or a `make` driving the
; docker on PATH gets `unknown flag: --build` and every `docker build` is the legacy builder.
;
; Same Tasks: pathentry as the entry above, and that is the rule rather than a convenience: this
; variable is read by every docker.exe a shell runs and carries config.json, the contexts and the
; docker login credentials with it. Pointing it here is honest exactly when this install owns the
; docker command, which is what the PATH entry means. A user who declined that checkbox is left
; alone. DockerConfigEntry.Ensure applies the same rule at tray startup, for an install made before
; DD124 — this half is what makes a fresh install correct before the tray has ever run.
;
; uninsdeletevalue, because a value naming a directory the uninstaller deleted is worse than none:
; the next docker.exe on that machine would read a config directory that is not there.
Root: HKCU; Subkey: "Environment"; ValueType: string; ValueName: "DOCKER_CONFIG"; \
    ValueData: "{app}"; Tasks: pathentry; Flags: uninsdeletevalue

; The docker-desktop:// handler (DD126). Buildx ends every build with
; `View build details: docker-desktop://dashboard/build/<builder>/<node>/<ref>`; the line is
; hardcoded in the binary this project pins and nothing configures it away — measured against
; DOCKER_CLI_HINTS, BUILDX_EXPERIMENTAL and BUILDX_NO_DEFAULT_ATTESTATIONS. The ref in it is real
; and names a record the daemon kept, so only the address was dead.
;
; This is another vendor's scheme, and taking it is the one thing here that argues against: Wsl's
; ToWindowsPath already refuses to map Docker Desktop's paths on the grounds that it would be this
; tool claiming another engine's layout. What answers it — HKCU rather than HKCR, so this is one
; user's choice and needs no elevation; uninsdeletekey, so it leaves when this does; the preflight
; refuses to install beside a rival at all; and a Docker Desktop installed afterwards overwrites
; this, which is the right way round for the conflict to resolve.
;
; Unconditional rather than gated on a task: a handler that is registered only sometimes is a link
; that works on some machines, which is worse to diagnose than one that never worked.
Root: HKCU; Subkey: "Software\Classes\docker-desktop"; ValueType: string; \
    ValueName: ""; ValueData: "URL:Docker build details"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\docker-desktop"; ValueType: string; \
    ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\docker-desktop\DefaultIcon"; ValueType: string; \
    ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
; "%1" quoted, because the URL is one argument and an unquoted one splits on the first space.
Root: HKCU; Subkey: "Software\Classes\docker-desktop\shell\open\command"; ValueType: string; \
    ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --open-build ""%1"""

[Run]
; postinstall and checked by default: what the user just installed is an icon, and not starting it
; leaves them looking at nothing. skipifsilent, because an unattended install pushed to a machine
; must not make a tray icon appear in somebody's session.
Filename: "{app}\{#MyAppExeName}"; Description: "Start {#MyAppName} now"; \
    Flags: nowait postinstall skipifsilent

; DD154 is the self-update this file used to say did not exist, and it is the one silent install that
; has to relaunch: the tray closed itself so it could be replaced, and a user who pressed Install and
; got no icon back would read that as an update that broke the product.
;
; So the flag stays off above and the exception is asked for explicitly, by the switch the updater
; passes and nobody else does. A silent install with no /RELAUNCH is still silent all the way through
; — which is what keeps an unattended deployment from putting an icon in somebody's session.
;
; runasoriginaluser because the whole install is per-user under LOCALAPPDATA: an elevated Setup
; relaunching this would leave the tray, its named pipe and its window owned by the wrong account.
; A test holds this switch equal to ReleaseUpdate.SilentArguments.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait runasoriginaluser; Check: RelaunchAsked

[Code]
const
  // The one distribution this product owns, spelled here and in EnginePaths.CurrentDistribution,
  // with a test holding the two equal. The uninstall unregisters it by name rather than deriving
  // which one is there: a derivation that got it wrong would leave a distribution no uninstaller
  // knows about.
  DistroName = 'freewilly';

  // Every step EngineProvisioner runs, so the bar below is a count rather than a guess. A test holds
  // this equal to ProvisioningStep's member count: a step added there and not here leaves a
  // successful install with a bar that never reaches the end, which reads as a failure.
  ProvisioningSteps = 11;

var
  // Built in InitializeWizard and shown from CurStepChanged, which is the only order Setup supports
  // — a custom page cannot be created once the wizard is running.
  ProvisionPage: TOutputProgressWizardPage;
  ProvisionLogPath: string;
  ProvisionStepsSeen: Integer;
  ProvisionLastLine: string;

  // DD123. The tasks page, drawn here rather than by Setup, and the four boxes on it.
  TasksPage: TWizardPage;
  WantDesktopIcon, WantStartupIcon, WantEngine, WantPathEntry: TNewCheckBox;

  // DD130. The preflight page, which stands between the tasks page and wpReady — so the last thing
  // Setup does before the first file is written is read the machine.
  PreflightPage: TWizardPage;
  PreflightHeading, PreflightFooter: TNewStaticText;
  PreflightMemo: TNewMemo;

  // DD131. What turns a message box into a page somebody can act on: the command, selectable and
  // copyable; the instructions Microsoft keeps; and the button that closes the loop between fixing
  // something and finding out whether it worked.
  PreflightCommandBox: TNewEdit;
  PreflightCopy, PreflightAgain: TNewButton;
  PreflightLink: TNewLinkLabel;

  // DD132. The button that runs the command rather than leaving it to be typed, and the three
  // things the page has to remember about what happened when it did.
  PreflightTurnOn: TNewButton;
  PreflightRefused: Boolean;
  PreflightFeatureOn: Boolean;
  PreflightRestartWanted: Boolean;

  // Asked once and remembered, because the wizard walks over this page in both directions and
  // re-reading the machine on every Back would be a pause with no new answer in it.
  PreflightAsked: Boolean;
  PreflightClear: Boolean;

  // What blocks, in the words the product chose. One string, shown on the page and written to the
  // file — the page has to be able to stand in for the file, because on a fresh install there is no
  // {app} to write one into.
  PreflightSaid: string;
  PreflightReport: string;

  // DD146. Every row as the product judged it, which is what a successful install keeps: the page
  // is about what blocks, and the file is about what the machine looked like.
  PreflightRows: string;

  // DD131. The two things the page can offer beyond the rows: the command a remedy names, and
  // whether the row that blocked is the one most readers will not recognise the name of.
  PreflightCommand: string;
  PreflightWsl2: Boolean;

/// Whether the thing that started Setup asked for the app to be launched again (DD154).
function RelaunchAsked: Boolean;
begin
  // Silent only. An interactive install already offers "Start FreeWilly now" as a checkbox, and a
  // run that answered both would start two trays — the second raises the first one's window and
  // exits (DD81), so it is harmless and still not what either entry means.
  //
  // CompareText rather than =: the switch is typed by a program, but a maintainer reproducing a
  // self-update by hand should not be defeated by /relaunch=YES.
  Result := WizardSilent and (CompareText(ExpandConstant('{param:RELAUNCH|no}'), 'yes') = 0);
end;

// ---------------------------------------------------------------------------------------------
// The tasks page, drawn rather than asked for (DD123)
// ---------------------------------------------------------------------------------------------
//
// Setup's own Select Additional Tasks page draws each box narrower than the glyph it holds: an
// unticked one is a vertical sliver and a ticked one a fragment of a check. Measured on Windows 11
// at 3840x2160, 200% scaling — and reproduced with Inno Setup 6.7.3's factory defaults and nothing
// of this script in them, so it is `TNewCheckListBox` and not anything here.
//
// Three of the four tasks are on by default, and one of them spends a quarter of a gigabyte of
// somebody's connection. A page where the reader cannot make out which boxes are ticked is not a
// page they can decline anything on, and dropping choices to dodge a drawing bug would trade an
// unreadable wizard for a decision taken on their behalf.
//
// So the page is rebuilt out of plain TNewCheckBox controls, which draw correctly at the same
// scaling — measured on this project's own uninstall page (DD121).
//
// [Tasks] STAYS, and that is what makes this small. It remains the source of truth: `Tasks:`
// parameters in [Files], [Icons] and [Registry] keep working untouched, `/MERGETASKS` keeps
// working for unattended installs, and a silent install never reaches this page at all. All this
// does is skip the broken page and hand Setup the same answer through WizardSelectTasks.

/// One of Setup's own messages, with the placeholders its own pages would have filled in.
function Message(const Id: string): string;
begin
  // SetupMessage hands back the raw string, and the standard messages carry [name] and [name/ver]
  // for the page that shows them to substitute. Setup's own pages do it; a page built here has to,
  // or the wizard reads "...while installing [name], then click Next" in every language shipped.
  // Measured on the page below before this existed.
  Result := Id;
  StringChange(Result, '[name/ver]', '{#MyAppName} {#MyAppVersion}');
  StringChange(Result, '[name]', '{#MyAppName}');
end;

/// Add one checkbox under a group heading, and answer the Y the next control starts at.
function TaskBox(Page: TWizardPage; const Group, Caption: string; Ticked: Boolean;
                 Top: Integer; var Box: TNewCheckBox): Integer;
var
  Heading: TNewStaticText;
begin
  Heading := TNewStaticText.Create(Page);
  Heading.Parent := Page.Surface;
  Heading.Left := 0;
  Heading.Top := Top;
  Heading.Caption := Group;

  Box := TNewCheckBox.Create(Page);
  Box.Parent := Page.Surface;

  // Width before Caption, and both after the control exists: the same ordering rule DD121 measured
  // on the uninstall page, where a caption assigned to a control that had not been given its width
  // wrapped at a column of zero.
  Box.Left := ScaleX(8);
  Box.Top := Heading.Top + Heading.Height + ScaleY(6);
  Box.Width := Page.SurfaceWidth - ScaleX(8);
  Box.Height := ScaleY(17);
  Box.Caption := Caption;
  Box.Checked := Ticked;

  Result := Box.Top + Box.Height + ScaleY(12);
end;

procedure BuildTasksPage;
var
  Intro: TNewStaticText;
  Y: Integer;
begin
  // Positioned after the directory page, which is exactly where Setup's own tasks page stands in
  // this wizard — there is no components page and DisableProgramGroupPage takes the other one.
  TasksPage := CreateCustomPage(
    wpSelectDir, SetupMessage(msgWizardSelectTasks), SetupMessage(msgSelectTasksDesc));

  Intro := TNewStaticText.Create(TasksPage);
  Intro.Parent := TasksPage.Surface;
  Intro.Left := 0;
  Intro.Top := 0;
  Intro.Width := TasksPage.SurfaceWidth;
  Intro.WordWrap := True;
  Intro.AutoSize := True;
  Intro.Caption := Message(SetupMessage(msgSelectTasksLabel2));
  Y := Intro.Top + Intro.Height + ScaleY(16);

  // The captions are Setup's own messages where Setup has one, so a translation this script never
  // wrote still reaches the page. A PackagingTests case holds each of these equal to the
  // [Tasks] description it stands in for — two spellings of one string is how a box ends up
  // promising something other than what ticking it does.
  // Which boxes start ticked, stated here because it cannot be asked for. WizardIsTaskSelected is
  // the obvious reader and it does not work from a page that replaces the one it reads: Setup fills
  // its task list while preparing wpSelectTasks, this skips that page, and the function answered
  // False for all four — measured, on this page, with every box coming up empty.
  //
  // So it is the same shape as DistroName and ProvisioningSteps elsewhere in this file: a value
  // restated in Pascal beside the section that owns it, with a PackagingTests case holding the two
  // equal. `Flags: unchecked` on the desktop icon and nothing on the other three is the fact, and
  // that test is what notices if either side moves. Getting it wrong is not theoretical — the
  // desktop icon came up ticked on the first build of this page.
  Y := TaskBox(TasksPage, ExpandConstant('{cm:AdditionalIcons}'),
       ExpandConstant('{cm:CreateDesktopIcon}'), False, Y, WantDesktopIcon);
  Y := TaskBox(TasksPage, 'Startup:',
       'Start {#MyAppName} with Windows', True, Y, WantStartupIcon);
  Y := TaskBox(TasksPage, 'Container engine:',
       'Download and install the container engine (about 250 MB)', True, Y, WantEngine);
  TaskBox(TasksPage, 'Command line:',
       'Put docker and freewilly on my PATH', True, Y, WantPathEntry);
end;

/// One task's term, named whether it was ticked or not.
function Term(const Name: string; Ticked: Boolean): string;
begin
  // Both directions, always. Naming only the ticked ones would leave a default standing through an
  // untick, and the default that costs somebody a quarter of a gigabyte is one of these four.
  if Ticked then
    Result := Name + ','
  else
    Result := '!' + Name + ',';
end;

/// Hand Setup the answer this page collected, in the spelling /MERGETASKS uses.
function ChosenTasks: string;
begin
  Result := Term('desktopicon', WantDesktopIcon.Checked)
          + Term('startupicon', WantStartupIcon.Checked)
          + Term('engine', WantEngine.Checked)
          + Term('pathentry', WantPathEntry.Checked);

  // The trailing comma an empty final term would otherwise leave.
  Result := Copy(Result, 1, Length(Result) - 1);
end;

// ---------------------------------------------------------------------------------------------
// PATH
// ---------------------------------------------------------------------------------------------

function PathEntryMissing: Boolean;
var
  Current: string;
begin
  // Idempotent: a reinstall must not append the same folder a second time. Semicolons on both ends
  // so \FreeWilly\bin is not matched inside \FreeWilly\bin2.
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Current) then
    Current := '';
  Result := Pos(';' + Lowercase(ExpandConstant('{app}\bin')) + ';',
                ';' + Lowercase(Current) + ';') = 0;
end;

procedure RemovePathEntry;
var
  Current, Wanted: string;
  P: Integer;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Current) then
    Exit;
  Wanted := ExpandConstant('{app}\bin');
  P := Pos(Lowercase(';' + Wanted), Lowercase(Current));
  if P > 0 then
    Delete(Current, P, Length(Wanted) + 1)
  else
  begin
    P := Pos(Lowercase(Wanted + ';'), Lowercase(Current));
    if P > 0 then
      Delete(Current, P, Length(Wanted) + 1)
    else
    begin
      P := Pos(Lowercase(Wanted), Lowercase(Current));
      if P = 0 then Exit;
      Delete(Current, P, Length(Wanted));
    end;
  end;
  RegWriteExpandStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Current);
end;

// ---------------------------------------------------------------------------------------------
// The preflight, run before the first file rather than after the last (DD130)
// ---------------------------------------------------------------------------------------------
//
// The order is the whole of this section. It used to run at ssPostInstall — after every file had
// been written, the PATH entry made and the Run value set — so a laptop without WSL2 received a
// complete installation of a tool whose one job it cannot do, plus a message box explaining that.
// Skipping the engine download was the only thing the late check still bought.
//
// It is still the product answering. `--preflight` is the same code a user runs when a working
// setup stopped working, and a second opinion written in Pascal would be two reports about one
// machine that a reader has to reconcile. What moved is when it is asked, not who answers.
//
// `--json` and not the text form, and the reason is the encoding. The report a person reads is
// UTF-8 with em dashes and arrows in it; Inno reads a file as ANSI, so pasting that into a control
// is the mojibake the old message box was written around. System.Text.Json escapes everything
// outside ASCII as \uXXXX, so the JSON form arrives intact and Unquote below puts the characters
// back — which is how the page can show what the report says rather than point at a file.

/// The four hex digits of a \u escape, as a number.
function Nibbles(const S: string): Integer;
var
  I, Digit: Integer;
  C: Char;
begin
  Result := 0;
  for I := 1 to Length(S) do
  begin
    C := Uppercase(S)[I];
    if (C >= '0') and (C <= '9') then
      Digit := Ord(C) - Ord('0')
    else if (C >= 'A') and (C <= 'F') then
      Digit := 10 + Ord(C) - Ord('A')
    else
      Digit := 0;
    Result := (Result * 16) + Digit;
  end;
end;

/// The one character with this code point.
function CodePoint(Value: Integer): string;
var
  Bytes: AnsiString;
begin
  // Chr here is a byte, whatever the string it is assigned into. Measured: — came back as
  // #$14 and the em dash was simply gone from the report. So the character is spelled out in
  // UTF-8 — which is bytes all the way down — and Utf8Decode turns those into the one character
  // a control can draw.
  //
  // Three bytes is as far as this goes, which covers the whole Basic Multilingual Plane and
  // therefore every dash, arrow and accent the product's own rows are written with. A surrogate
  // pair would arrive here as two halves and leave as two replacement characters; nothing in a
  // preflight row is outside the BMP, and a report that needed an emoji would have a worse problem.
  if Value < $80 then
    Bytes := Chr(Value)
  else if Value < $800 then
    Bytes := Chr($C0 or (Value shr 6)) + Chr($80 or (Value and $3F))
  else
    Bytes := Chr($E0 or (Value shr 12))
           + Chr($80 or ((Value shr 6) and $3F))
           + Chr($80 or (Value and $3F));

  Result := Utf8Decode(Bytes);
end;

/// One JSON string literal's contents, with its escapes turned back into characters.
function Unquote(const S: string): string;
var
  I: Integer;
begin
  Result := '';
  I := 1;
  while I <= Length(S) do
  begin
    if (S[I] = '\') and (I < Length(S)) then
    begin
      I := I + 1;
      case S[I] of
        // A newline inside a Detail is a line break on the page. \r is dropped rather than emitted,
        // because the pair would otherwise become two breaks where the writer meant one.
        'n': Result := Result + #13#10;
        'r': ;
        't': Result := Result + ' ';
        'u':
          begin
            Result := Result + CodePoint(Nibbles(Copy(S, I + 1, 4)));
            I := I + 4;
          end;
      else
        // \" \\ \/ and anything this does not know: the character itself, which is the right answer
        // for every escape JSON defines apart from the four above.
        Result := Result + S[I];
      end;
    end
    else
      Result := Result + S[I];
    I := I + 1;
  end;
end;

/// The value of the named property, if this line carries it.
function JsonValue(const Raw, Name: string; var Value: string): Boolean;
var
  Line, Head: string;
begin
  // WriteIndented puts one property on one line, which is what makes a line reader honest here
  // rather than a parser this file would have to be trusted with. A string value cannot contain a
  // raw newline — JSON escapes it — so a property never spans two lines whatever it holds.
  Result := False;
  Line := Trim(Raw);
  Head := '"' + Name + '": ';
  if Pos(Head, Line) <> 1 then
    Exit;

  Value := Copy(Line, Length(Head) + 1, MaxInt);

  // The comma every property but an object's last one carries.
  if (Length(Value) > 0) and (Value[Length(Value)] = ',') then
    Value := Copy(Value, 1, Length(Value) - 1);

  // A string, unquoted; null, true and false are handed back as written, which is what the caller
  // compares Blocks against.
  if (Length(Value) >= 2) and (Value[1] = '"') and (Value[Length(Value)] = '"') then
    Value := Unquote(Copy(Value, 2, Length(Value) - 2));

  Result := True;
end;

/// What a remedy spells in backticks, which is the command a reader has to type.
function Backticked(const Remedy: string): string;
var
  Opened, Closed: Integer;
begin
  // The product writes its remedies with the command in backticks — `wsl.exe --install
  // --no-distribution`, `wsl --update`, `docker context use default` — so the page does not have to
  // know which row it is looking at to find the thing worth copying. One source of truth for the
  // command, and it is the one that decided the remedy (DD131).
  Result := '';
  Opened := Pos('`', Remedy);
  if Opened = 0 then
    Exit;

  Closed := Pos('`', Copy(Remedy, Opened + 1, MaxInt));
  if Closed = 0 then
    Exit;

  Result := Copy(Remedy, Opened + 1, Closed - 1);
end;

/// Read the report and build the one paragraph per blocking row that the page and the file share.
procedure ReadTheVerdict(const Path: string);
var
  Lines: TArrayOfString;
  I: Integer;
  Value, Id, Title, Detail, Remedy, Verdict: string;
begin
  PreflightSaid := '';
  PreflightRows := '';
  PreflightCommand := '';
  PreflightWsl2 := False;
  if not LoadStringsFromFile(Path, Lines) then
    Exit;

  Id := '';
  Title := '';
  Detail := '';
  Remedy := '';
  Verdict := '';
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    if JsonValue(Lines[I], 'Id', Value) then
      Id := Value
    else if JsonValue(Lines[I], 'Title', Value) then
      Title := Value
    else if JsonValue(Lines[I], 'Verdict', Value) then
      Verdict := Value
    else if JsonValue(Lines[I], 'Detail', Value) then
      Detail := Value
    else if JsonValue(Lines[I], 'Remedy', Value) then
      Remedy := Value
    else if JsonValue(Lines[I], 'Blocks', Value) then
    begin
      // Blocks is the row's own answer to "does this stop an install right now", and it is the last
      // property of the object — so by the time it is read, the four above belong to this row.
      // Reading Verdict and Blocking here and deciding again in Pascal is the second opinion this
      // section exists not to have.
      if Value = 'true' then
      begin
        if PreflightSaid <> '' then
          PreflightSaid := PreflightSaid + #13#10;
        PreflightSaid := PreflightSaid + Title + '  ' + Detail + #13#10;
        if (Remedy <> '') and (Remedy <> 'null') then
        begin
          PreflightSaid := PreflightSaid + '    -> ' + Remedy + #13#10;

          // The first blocking row's command, because the page has one box and the rows are in the
          // order they are meant to be read — so the first one is the first thing to do.
          if PreflightCommand = '' then
            PreflightCommand := Backticked(Remedy);
        end;

        // Named rather than inferred from the wording, so a rephrased remedy cannot quietly turn
        // the WSL2 page back into the generic one.
        if Id = 'wsl2' then
          PreflightWsl2 := True;
      end;

      // Every row and not only the blocking ones (DD146). What blocks is what the page is for;
      // what the machine looked like is what the file is for, and a file holding only the rows
      // that failed cannot answer "was this green when it was installed", which is the first
      // question anybody asks of it months later.
      //
      // The verdict is carried through rather than decided here. Judging a row in Pascal is the
      // second opinion this section exists not to have; laying one out is not, and the page above
      // already lays them out this way.
      PreflightRows := PreflightRows
        + '  [' + Verdict + ']  ' + Title + '  ' + Detail + #13#10;
      if (Remedy <> '') and (Remedy <> 'null') and (Verdict <> 'Pass') then
        PreflightRows := PreflightRows + '      -> ' + Remedy + #13#10;

      Id := '';
      Title := '';
      Detail := '';
      Remedy := '';
      Verdict := '';
    end;
  end;
end;

/// Where a report outlives Setup. {tmp} does not, and that is the whole of this function.
function ReportDirectory: string;
begin
  // {app} while there is one — a reinstall, or an upgrade — because that is where somebody looks
  // for this product's own files. On a fresh install blocked by this check there is no {app} and
  // there must not be: nothing has been written yet, which is the point. TEMP is the user's own and
  // survives Setup exiting, unlike {tmp}, which Setup deletes on its way out.
  Result := ExpandConstant('{app}');
  if not DirExists(Result) then
    Result := ExpandConstant('{%TEMP}');
end;

/// Write the report where it can be read after Setup has closed.
procedure KeepTheReport(const Path: string);
var
  Written: TArrayOfString;
begin
  // UTF-8 and not SaveStringToFile, because the rows carry whatever characters the product chose
  // for them — an em dash in a detail, an arrow in a remedy — and an ANSI write turns those into
  // question marks.
  SetArrayLength(Written, 5);
  Written[0] := 'FreeWilly preflight';
  Written[1] := '';

  // One sentence, and it is the verdict rather than a guess at what the reader is doing: a report
  // in {app} on a machine that cleared is a record, and the same file in TEMP on one that did not
  // is the whole of what happened.
  if PreflightClear then
    Written[2] := 'This machine can host the container engine. Every row as it read at install time:'
  else
    Written[2] := 'This machine cannot host the container engine yet. Nothing was installed.';

  Written[3] := '';
  Written[4] := PreflightRows;
  SaveStringsToUTF8File(Path, Written, False);
end;

/// Read this machine. Answers whether an engine can be hosted on it, and changes nothing.
function Preflight: Boolean;
var
  Code: Integer;
  Machine, Verdict: string;

  // LoadStringFromFile hands back bytes, not text. Only the branch below reads it, and only to
  // quote back whatever the verb printed instead of a report — which is a message from a program
  // that just failed, so ASCII is the safe assumption and mojibake would be the least of it.
  Raw: AnsiString;
begin
  if PreflightAsked then
  begin
    Result := PreflightClear;
    Exit;
  end;

  PreflightAsked := True;
  PreflightClear := False;
  PreflightReport := AddBackslash(ReportDirectory) + 'preflight.txt';

  // The one file [Files] already carries, put in {tmp} at the cost of a decompress. Nothing is
  // installed by this and nothing outside {tmp} is touched, which is what lets the check run before
  // the wizard has committed to anything.
  ExtractTemporaryFile('{#MyAppExeName}');
  Machine := ExpandConstant('{tmp}\{#MyAppExeName}');
  Verdict := ExpandConstant('{tmp}\preflight.json');

  // Redirected through cmd, which is the only form of a console verb an installer should use: a
  // windowed executable hands its output to whatever holds its standard handles, and here that is
  // this file. 2>&1 so a run that fails before it prints a report still leaves its reason in the
  // file, which is what the unknown-code branch below shows.
  if not Exec(ExpandConstant('{cmd}'),
              '/C ""' + Machine + '" --preflight --json > "' + Verdict + '" 2>&1"',
              '', SW_HIDE, ewWaitUntilTerminated, Code) then
  begin
    // It never ran, so there is no verdict. Treated as a block: the alternative is installing on
    // the strength of a check that did not happen.
    PreflightSaid := 'The check could not be started on this machine.';
    Result := False;
    Exit;
  end;

  // 0 means every blocking row is green and 1 means at least one is not — the two the verb
  // documents. Anything else is a usage error or a crash, and the file holds whatever it said.
  ReadTheVerdict(Verdict);
  PreflightClear := Code = 0;
  if (Code > 1) or ((Code = 1) and (PreflightSaid = '')) then
  begin
    Raw := '';
    LoadStringFromFile(Verdict, Raw);
    PreflightSaid := 'The check answered ' + IntToStr(Code) + ' and named nothing:'
                   + #13#10#13#10 + Raw;
  end;

  Result := PreflightClear;

  // Written whatever the verdict was (DD146). A blocked install needs the file because it is all
  // there is; a successful one needs it because "was this row green when it was installed" is the
  // first question anybody asks months later, and DD130 left that question unanswerable by moving
  // the write in front of the copy along with the read.
  KeepTheReport(PreflightReport);
end;

// ---------------------------------------------------------------------------------------------
// The page it is read on
// ---------------------------------------------------------------------------------------------

// >>> page-probe (DD145)
//
// Everything between this marker and its closing one is compiled a second time, on its own, by
// tests\FreeWilly.Cases\PreflightPage.cs: it wraps this block in a Setup that shows the page,
// reports every control's rectangle, and closes itself. That is what turns "read the Pascal and
// hope" into a check — and the two failures below were both found that way rather than by reading.
//
// It was scripts\page-probe.ps1 until WW87. What moved is only the checking: the rectangles now go
// out in winwright's geometry dump format and the engine reads them, which is why nothing on that
// side states a coordinate any more.
//
// The markers are machine-readable on purpose. The harness used to slice on a section heading,
// which is prose, and prose is renamed by whoever is tidying up.

// The page a blocked install lands on (DD131).
//
// What a blocked machine used to get was a message box naming `wsl.exe --install
// --no-distribution` and a path to a text file. That is exactly right for a reader who already
// knows what WSL2 is, and it is the whole of the experience for a reader who does not: the term is
// never expanded, the command is in a box that cannot be copied from, and there is no way to find
// out whether the fix worked short of running Setup again.
//
// So the page says four things in order — what the feature is, the numbered steps, the command
// itself, and where Microsoft documents it — and carries the button that closes the loop. Check
// again re-reads the machine in place and releases Next the moment nothing blocks, which turns
// fix-and-find-out from one reinstall into one click.

/// What the feature is, for a reader who has never heard of it.
function Wsl2InPlainWords: string;
begin
  Result :=
    'WSL2 is the Windows feature that runs a real Linux kernel beside Windows. A container is a '
  + 'Linux process, so until the feature is on there is nothing for an engine to run one in — '
  + 'which is why Setup stops here rather than installing a tool that could not work.';
end;

/// The steps, each one action long.
function Wsl2Steps: string;
begin
  // Numbered rather than prose, because this is read by somebody following it a line at a time
  // rather than reading it. Step 3 is the one that gets skipped and the one that decides whether
  // any of the rest worked, so it says what happens if it is.
  Result :=
    '1.  Open Terminal as an administrator: right-click the Start button and choose'#13#10
  + '     "Terminal (Admin)", or "Windows PowerShell (Admin)" on Windows 10.'#13#10#13#10
  + '2.  Run the command below. It turns the feature on and installs no Linux'#13#10
  + '     distribution — this product brings its own.'#13#10#13#10
  + '3.  Restart Windows. The feature is not on until you do, and Check again will'#13#10
  + '     still say so if you skip it.'#13#10#13#10
  + '4.  Come back to this page and choose Check again.';
end;

/// Put the command on the clipboard.
procedure CopyTheCommand(Sender: TObject);
var
  Code: Integer;
begin
  // Setup has no clipboard of its own — there is no SetClipboardText in Pascal Script — so this is
  // clip.exe, which ships with Windows. `<nul set /p=` rather than `echo`, and the difference
  // matters here: echo appends a newline, and a newline on the clipboard turns a paste into an
  // administrator terminal into a command that has already run before it could be read.
  Exec(ExpandConstant('{cmd}'),
       '/C <nul set /p="' + PreflightCommand + '"| clip',
       '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

/// Open the page Microsoft keeps about it.
procedure OpenTheInstructions(Sender: TObject; const Link: string; LinkType: TSysLinkType);
var
  Code: Integer;
begin
  ShellExec('open', Link, '', '', SW_SHOWNORMAL, ewNoWait, Code);
end;

// ---------------------------------------------------------------------------------------------
// Turning the feature on (DD132)
// ---------------------------------------------------------------------------------------------
//
// Docker Desktop's installer does this and its logs name the step — EnableFeaturesAction,
// "Required features: VirtualMachinePlatform, Microsoft-Windows-Subsystem-Linux". The difference
// between the two products here was never knowledge: Docker Desktop runs elevated from its first
// dialog, so turning a Windows feature on costs it nothing extra.
//
// PrivilegesRequired=lowest is the whole point of this installer and is not being reversed for
// this. The elevation is bought per step instead: one `runas` on the command the row already
// named, raised only after somebody presses the button that asks for it, and refused without
// consequence. Installing the application still prompts for nothing.
//
// What is deliberately NOT here is a fallback. `--no-distribution` does not exist on older WSL
// builds, and the generous repair — dropping the flag and running `wsl --install` — installs
// Ubuntu on somebody's machine uninvited. So this file names no command of its own at all: it runs
// the one the remedy spelled, and measured, wsl.exe rejects a flag it does not know
// ("Invalid command line argument") without installing anything. A build too old for the flag gets
// a visible failure and the steps it already had, which is the safe direction to be wrong in.

/// Whether Windows is already waiting for a restart to finish servicing something.
function RestartIsPending: Boolean;
begin
  // Written by Component Based Servicing when a Windows feature is enabled, and readable without
  // elevation — which is what makes it usable here. ShellExec cannot hand back an elevated child's
  // exit code, so this is the one piece of evidence available about whether the run did anything.
  Result := RegKeyExists(
    HKEY_LOCAL_MACHINE,
    'SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending');
end;

/// Split a command line into the program and the rest.
procedure SplitCommand(const Line: string; var Exe, Arguments: string);
var
  Space: Integer;
begin
  Space := Pos(' ', Line);
  if Space = 0 then
  begin
    Exe := Line;
    Arguments := '';
    Exit;
  end;

  Exe := Copy(Line, 1, Space - 1);
  Arguments := Copy(Line, Space + 1, MaxInt);
end;

/// A bare program name, resolved where Windows keeps its own.
function InSystem32(const Exe: string): string;
begin
  // Setup is a 32-bit process, so a bare name found through PATH resolves against SysWOW64 — and
  // wsl.exe is a 64-bit tool that exists only in the real System32. {sys} is the constant that
  // knows the difference, which is why the same expansion is what runs tasklist and taskkill in
  // the uninstall below.
  Result := Exe;
  if (Pos('\', Exe) > 0) or (Pos('/', Exe) > 0) then
    Exit;

  if Pos('.', Exe) = 0 then
    Result := Exe + '.exe';

  Result := ExpandConstant('{sys}\') + Result;
end;

/// Leave Setup where the restart will find it.
procedure ArrangeToBePickedUp;
begin
  // RunOnce and not Run: this fires exactly once and deletes itself, so the worst case of somebody
  // turning the feature on and then abandoning the install is one wizard they can close. {srcexe}
  // is where Setup was actually started from, which survives the restart when a copy in {tmp}
  // would not.
  RegWriteStringValue(
    HKEY_CURRENT_USER,
    'Software\Microsoft\Windows\CurrentVersion\RunOnce',
    'FreeWilly Setup',
    '"' + ExpandConstant('{srcexe}') + '"');
end;

/// What the page says once the elevated run has been through.
function AfterTheRun: string;
begin
  if PreflightRestartWanted then
    Result :=
      'The feature is on. Windows is now waiting for a restart to finish putting it in place, '
    + 'and until that happens WSL2 is still not usable — Check again will keep saying so.'
  else
    Result :=
      'The command has run. Windows needs a restart before the feature is usable, and this page '
    + 'cannot read the result of an elevated command — so if the window that opened reported an '
    + 'error, the steps above are still the way through.';

  Result := Result + #13#10#13#10
    + 'Choose Restart now when you are ready. Setup will reopen once by itself after the restart, '
    + 'so there is nothing to remember.';
end;

/// Show what the last read found, and enable Next only if nothing blocks.
procedure ShowTheVerdict;
var
  Y, Bottom, Row: Integer;
begin
  // Laid out from the bottom, because what is optional is at the bottom and the prose has to take
  // whatever is left rather than a height guessed here. A page cannot scroll; the memo can.
  Bottom := PreflightPage.SurfaceHeight;

  PreflightFooter.Top := Bottom - PreflightFooter.Height;
  Row := PreflightFooter.Top - ScaleY(10) - ScaleY(23);

  PreflightAgain.Top := Row;
  PreflightLink.Top := Row + ScaleY(4);

  // Everything below is a remedy, and a remedy for a machine that no longer blocks is clutter on a
  // page whose whole message is that there is nothing left to do. Found by the harness (DD145): the
  // cleared page said "Nothing blocks an install any more" above a command box, a Copy button and a
  // link to Microsoft's instructions for installing a feature that is already installed.
  PreflightLink.Visible := PreflightWsl2 and not PreflightClear;

  // Offered only where there is a command to run and a row this file is willing to run one for.
  // Every other blocker — a rival engine, a firmware setting — is somebody else's to change, and a
  // button that elevated for those would be an installer taking a machine apart.
  PreflightTurnOn.Visible := PreflightWsl2 and (PreflightCommand <> '') and not PreflightClear;
  PreflightTurnOn.Top := Row;
  if PreflightFeatureOn then
    PreflightTurnOn.Caption := 'Restart now'
  else
    PreflightTurnOn.Caption := 'Turn it on for me';

  PreflightCommandBox.Visible := (PreflightCommand <> '') and not PreflightClear;
  PreflightCopy.Visible := PreflightCommandBox.Visible;
  if PreflightCommandBox.Visible then
  begin
    Row := Row - ScaleY(10) - PreflightCopy.Height;
    PreflightCopy.Top := Row;

    // Centred against the button rather than aligned to its top, because the two are not the same
    // height and never were: an edit sizes itself to its font — measured at 40 against the button's
    // 49 at 200% scaling — so a shared Top leaves the button hanging below the box it belongs to.
    PreflightCommandBox.Top :=
      Row + ((PreflightCopy.Height - PreflightCommandBox.Height) div 2);
    PreflightCommandBox.Text := PreflightCommand;
  end;

  Y := PreflightHeading.Top + PreflightHeading.Height + ScaleY(8);
  PreflightMemo.Top := Y;
  PreflightMemo.Height := Row - ScaleY(10) - Y;

  if PreflightClear then
  begin
    // Reachable only through Check again: the page is skipped when the first read was green.
    PreflightHeading.Caption := 'Nothing blocks an install any more';
    PreflightMemo.Text :=
      'This machine can host the container engine. Choose Next to carry on.';
  end
  else if PreflightFeatureOn then
  begin
    PreflightHeading.Caption := 'Windows needs to restart to finish turning it on';
    PreflightMemo.Text := AfterTheRun;
  end
  else if PreflightWsl2 then
  begin
    PreflightHeading.Caption := 'Windows needs one feature turned on first';
    PreflightMemo.Text := Wsl2InPlainWords + #13#10#13#10 + Wsl2Steps;

    // A refused prompt is not an error and the page does not treat it as one: the steps above are
    // exactly what the button would have done, and an account that cannot elevate at all lands
    // here too.
    if PreflightRefused then
      PreflightMemo.Text :=
        'Turn it on for me needs one administrator prompt, and that prompt was not granted. '
        + 'Nothing has changed, and the steps below do the same thing by hand.'#13#10#13#10
        + PreflightMemo.Text;
  end
  else
  begin
    PreflightHeading.Caption := 'This machine cannot host the container engine yet';
    PreflightMemo.Text :=
      'Nothing has been installed and nothing has been changed. Each row below names the one '
      + 'action that changes it.'#13#10#13#10 + PreflightSaid;
  end;

  PreflightFooter.Caption := 'Written to ' + PreflightReport;
  PreflightFooter.Visible := not PreflightClear;

  // The verdict decides whether Next is available at all, which is what makes this a stop rather
  // than a warning somebody clicks past onto a machine that cannot run what it is about to receive.
  WizardForm.NextButton.Enabled := PreflightClear;
end;

/// Run the command the row named, once, elevated.
procedure TurnItOn;
var
  Exe, Arguments: string;
  Shell: Integer;
  WasPending: Boolean;
begin
  SplitCommand(PreflightCommand, Exe, Arguments);
  WasPending := RestartIsPending;

  PreflightTurnOn.Enabled := False;
  try
    // SW_SHOWNORMAL, deliberately: `wsl --install` takes minutes and prints as it goes, and a
    // hidden window would leave the wizard frozen with nothing anywhere saying why. It is also the
    // only place an error from the command is ever seen, since ShellExec cannot hand back the exit
    // code of a process it elevated.
    if not ShellExec('runas', InSystem32(Exe), Arguments, '',
                     SW_SHOWNORMAL, ewWaitUntilTerminated, Shell) then
    begin
      // The prompt was refused, or this account cannot elevate at all. Neither is an error worth
      // a dialog — the page already carries the steps that do the same thing.
      PreflightRefused := True;
      Exit;
    end;
  finally
    PreflightTurnOn.Enabled := True;
  end;

  PreflightRefused := False;
  PreflightFeatureOn := True;

  // Windows itself now wanting a restart, where it did not before, is the one piece of evidence
  // available that the run enabled something. Already-pending is not read as proof: plenty of
  // other things set that key, and claiming a feature is on because Windows Update is halfway
  // through would send somebody to reboot for nothing.
  PreflightRestartWanted := RestartIsPending and not WasPending;

  ArrangeToBePickedUp;
end;

/// Restart Windows, having said that this is what the button does.
procedure RestartNow;
var
  Code: Integer;
begin
  // Five seconds rather than none, and a comment Windows shows in its own dialog: this closes
  // everything the user has open, so the one thing it must not be is instant and unattributed.
  Exec(ExpandConstant('{sys}\shutdown.exe'),
       '/r /t 5 /c "FreeWilly Setup is restarting Windows to finish turning on WSL2."',
       '', SW_HIDE, ewNoWait, Code);
end;

/// The one button, which does the next thing rather than a different thing.
procedure TurnItOnOrRestart(Sender: TObject);
begin
  if PreflightFeatureOn then
    RestartNow
  else
    TurnItOn;

  ShowTheVerdict;
end;

/// Read the machine again, without leaving the page.
procedure CheckAgain(Sender: TObject);
begin
  // The button that matters most. Forgetting the last answer is the whole of it: everything else
  // here already re-runs from the one function, so the loop between fixing something and finding
  // out is a click rather than a reinstall.
  PreflightAsked := False;

  PreflightAgain.Enabled := False;
  WizardForm.Cursor := crHourGlass;
  try
    Preflight;
  finally
    WizardForm.Cursor := crDefault;
    PreflightAgain.Enabled := True;
  end;

  ShowTheVerdict;
end;

procedure BuildPreflightPage;
begin
  // After the tasks page, which puts it immediately before wpReady — the last page before Setup
  // commits to anything. Shown only when something blocks: a machine that can host an engine gets
  // no extra click out of this, which is what ShouldSkipPage below is for.
  PreflightPage := CreateCustomPage(
    TasksPage.ID,
    'This machine',
    'What the engine needs, read before anything is written.');

  PreflightHeading := TNewStaticText.Create(PreflightPage);
  PreflightHeading.Parent := PreflightPage.Surface;
  PreflightHeading.Left := 0;
  PreflightHeading.Top := 0;
  PreflightHeading.AutoSize := True;
  PreflightHeading.Font.Style := [fsBold];
  PreflightHeading.Caption := 'This machine';

  PreflightMemo := TNewMemo.Create(PreflightPage);
  PreflightMemo.Parent := PreflightPage.Surface;
  PreflightMemo.Left := 0;
  PreflightMemo.Width := PreflightPage.SurfaceWidth;

  // Read-only and scrolling rather than a wrapping label: a remedy is a command somebody has to
  // type, and a control they can select and copy out of is worth more here than one that cannot be
  // clicked into. ScrollBars because the number of blocking rows is not this page's to bound, and
  // a wizard page has no scrollbar of its own to fall back on.
  PreflightMemo.ReadOnly := True;
  PreflightMemo.WordWrap := True;
  PreflightMemo.ScrollBars := ssVertical;

  // The command, selectable — which is the half of "copyable" that survives a Copy button nobody
  // notices. Read-only for the same reason the memo is: editing it would only produce a command
  // that is not the one the check asked for.
  PreflightCommandBox := TNewEdit.Create(PreflightPage);
  PreflightCommandBox.Parent := PreflightPage.Surface;
  PreflightCommandBox.Left := 0;
  PreflightCommandBox.Width := PreflightPage.SurfaceWidth - ScaleX(75 + 8);
  PreflightCommandBox.Height := ScaleY(23);
  PreflightCommandBox.ReadOnly := True;

  PreflightCopy := TNewButton.Create(PreflightPage);
  PreflightCopy.Parent := PreflightPage.Surface;
  PreflightCopy.Left := PreflightPage.SurfaceWidth - ScaleX(75);
  PreflightCopy.Width := ScaleX(75);
  PreflightCopy.Height := ScaleY(23);
  PreflightCopy.Caption := 'Copy';
  PreflightCopy.OnClick := @CopyTheCommand;

  // Microsoft's own instructions, and the same page Docker Desktop links for the same reason. A
  // link label rather than a printed URL: a URL on a wizard page is an address nobody can click
  // and most people will not retype.
  PreflightLink := TNewLinkLabel.Create(PreflightPage);
  PreflightLink.Parent := PreflightPage.Surface;
  PreflightLink.Left := 0;

  // Whatever the two buttons on this row leave. Stated as their geometry rather than as a number,
  // so widening a caption cannot quietly push the link underneath one of them.
  PreflightLink.Width := PreflightPage.SurfaceWidth - ScaleX(90 + 8 + 120 + 8);
  PreflightLink.Caption :=
    '<a href="https://learn.microsoft.com/en-us/windows/wsl/install">'
    + 'Microsoft''s instructions for installing WSL</a>';
  PreflightLink.OnLinkClick := @OpenTheInstructions;

  PreflightAgain := TNewButton.Create(PreflightPage);
  PreflightAgain.Parent := PreflightPage.Surface;
  PreflightAgain.Left := PreflightPage.SurfaceWidth - ScaleX(90);
  PreflightAgain.Width := ScaleX(90);
  PreflightAgain.Height := ScaleY(23);
  PreflightAgain.Caption := 'Check again';
  PreflightAgain.OnClick := @CheckAgain;

  // To the left of Check again, because it is the thing to do first and the thing to do after it
  // is to check. The caption is set by ShowTheVerdict — this button turns the feature on and then
  // restarts, and one button doing the next step is fewer than two of which one is always wrong.
  PreflightTurnOn := TNewButton.Create(PreflightPage);
  PreflightTurnOn.Parent := PreflightPage.Surface;
  PreflightTurnOn.Left := PreflightAgain.Left - ScaleX(8) - ScaleX(120);
  PreflightTurnOn.Width := ScaleX(120);
  PreflightTurnOn.Height := ScaleY(23);
  PreflightTurnOn.Caption := 'Turn it on for me';
  PreflightTurnOn.OnClick := @TurnItOnOrRestart;

  PreflightFooter := TNewStaticText.Create(PreflightPage);
  PreflightFooter.Parent := PreflightPage.Surface;
  PreflightFooter.Left := 0;
  PreflightFooter.Width := PreflightPage.SurfaceWidth;
  PreflightFooter.AutoSize := False;
  PreflightFooter.Height := ScaleY(13);
  PreflightFooter.Caption := '';
end;

// <<< page-probe (DD145)

// ---------------------------------------------------------------------------------------------
// Stopping what is already running (DD121, DD236)
// ---------------------------------------------------------------------------------------------
//
// Both paths need this and for the same reason: a file cannot be replaced or deleted while the
// process holding it is alive. DD121 wrote it for the uninstall, where an undeletable
// FreeWilly.exe leaves a root nobody owns and no uninstaller left to offer to take it. DD236 found
// the install failing on the same rock, having been left with only the backstop.
//
// The backstop is CloseApplications, and what it cannot do is the whole argument. Restart Manager
// closes a windowed application by asking its window to close; the tray usually has no window and
// the detached `--run` engine has none at all, so the graceful stop has to be asked for by name.
//
// Four steps, in this order. Stop the engine, ask the tray to go, check, and force only what is
// left.

var
  // Set by the tasklist callback, because a callback cannot return anything.
  SawTheProcess: Boolean;

  // Whether the install path has already done it. The stop is asked for twice on purpose — see
  // NextButtonClick and PrepareToInstall below — and this is what makes the second one free.
  AlreadyStopped: Boolean;

procedure TasklistSaid(const S: String; const Error, FirstLine: Boolean);
begin
  if Pos(Lowercase('{#MyAppExeName}'), Lowercase(S)) > 0 then
    SawTheProcess := True;
end;

function AnythingIsStillRunning: Boolean;
var
  Code: Integer;
begin
  // tasklist rather than a handle test: a running executable can still be renamed and can still be
  // opened for reading, so every cheap probe answers the wrong question. With no match it prints
  // "INFO: No tasks are running..." and the image name appears nowhere in it, which is the whole
  // decision below.
  SawTheProcess := False;
  if not ExecAndLogOutput(ExpandConstant('{sys}\tasklist.exe'),
       '/FI "IMAGENAME eq {#MyAppExeName}" /NH', '', SW_HIDE,
       ewWaitUntilTerminated, Code, @TasklistSaid) then
  begin
    // It could not be asked. Answering yes here would force-kill on no evidence, so this defers to
    // Restart Manager, which is exactly what CloseApplications is carried for.
    Result := False;
    Exit;
  end;

  Result := SawTheProcess;
end;

procedure StopEverything;
var
  Code: Integer;
  Exe: string;
begin
  Exe := ExpandConstant('{app}\{#MyAppExeName}');

  // Nothing to ask, and nothing that could have started the engine either. A root without the
  // executable in it is a fresh install, or one a previous uninstall got most of the way through.
  if not FileExists(Exe) then
    Exit;

  // The engine first. --stop ends the pipe relay and terminates the distribution, which is what
  // makes an unregister a clean one — an open virtual disk is a directory that survives its own
  // deletion. It also takes the detached `--run` process with it, since what that serves lives
  // inside the distribution being terminated.
  Exec(Exe, '--stop', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code);

  // Then the tray, by the verb written for this (DD121). It waits until the process is actually
  // gone rather than until the request was delivered, so returning from here means the file is no
  // longer open. A kill would leave the notification icon in the overflow until something hovers it.
  Exec(Exe, '--quit', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code);

  if not AnythingIsStillRunning then
    Exit;

  // The last resort. Reached by a process that ignored the verb or by a `--run` started outside the
  // tray; either way the alternative is the failure this whole section exists to remove.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName} /T',
       '', SW_HIDE, ewWaitUntilTerminated, Code);

  // Handles are closed by the kernel after the process is, and the first delete follows
  // immediately. Half a second costs nothing on the path where it was not needed.
  Sleep(500);
end;

/// The install's half of it: once, however many times it is asked for (DD236).
procedure StopBeforeInstalling;
begin
  if AlreadyStopped then
    Exit;

  AlreadyStopped := True;
  StopEverything;
end;

// ---------------------------------------------------------------------------------------------
// The wizard, wired to the two pages above
// ---------------------------------------------------------------------------------------------
//
// Down here rather than beside the pages they steer: Pascal Script resolves a name only if it has
// already been declared, so every event handler has to stand below everything it calls.

procedure InitializeWizard;
begin
  ProvisionPage := CreateOutputProgressPage(
    'Container engine',
    'Setup is putting the engine on this machine. Nothing is started by this.');

  // In this order, because the preflight page is positioned after the tasks page and cannot name a
  // page that does not exist yet.
  BuildTasksPage;
  BuildPreflightPage;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  // The broken one, and only ever this one. Skipped rather than removed, because [Tasks] is still
  // what every `Tasks:` parameter and every /MERGETASKS reads.
  if PageID = wpSelectTasks then
  begin
    Result := True;
    Exit;
  end;

  // A machine that can host an engine has nothing to read here, and a page saying so is a click
  // this wizard has not earned. The answer is already in hand: NextButtonClick below reads the
  // machine on the way out of the tasks page, which is the page immediately in front of this one.
  Result := (PageID = PreflightPage.ID) and PreflightAsked and PreflightClear;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = wpReady then
  begin
    // Before the Preparing page exists, which is the point of doing it here (DD236). That page is
    // where Restart Manager scans, and a graceful stop that ran after the scan would be a stop the
    // user had already been shown a failure dialog about. This is the last moment certainly
    // earlier than it, and Back off this page is not worth defending against: somebody on the
    // ready page pressing Install has asked for the thing that stops the tray.
    WizardForm.Cursor := crHourGlass;
    try
      StopBeforeInstalling;
    finally
      WizardForm.Cursor := crDefault;
    end;

    Exit;
  end;

  if CurPageID <> TasksPage.ID then
    Exit;

  WizardSelectTasks(ChosenTasks);

  // Read here, so the page after this one already knows whether it has anything to show. It costs a
  // decompress of the one file plus the probes the verb runs, and the cursor says so — a wizard
  // that stops responding for a second with no explanation is the same defect in a smaller form.
  WizardForm.Cursor := crHourGlass;
  try
    Preflight;
  finally
    WizardForm.Cursor := crDefault;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  // Every page but this one, so Next comes back the moment the wizard leaves: the page disables it
  // and only the page is entitled to. Without this, a Back off a blocked page would land on a
  // tasks page nobody can leave.
  if CurPageID <> PreflightPage.ID then
  begin
    WizardForm.NextButton.Enabled := True;
    Exit;
  end;

  ShowTheVerdict;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // The unattended half, and the only half an unattended install has: it never sees a page, so the
  // stop has to be here. Raised before Setup copies its first file, and returning a message aborts
  // with exit code 7 — "Preparing to Install determined that Setup cannot proceed" — which is a
  // deployment's answer and is distinct from every code a cancelled or failed install produces.
  // Measured on a probe installer: exit 7, and nothing written.
  //
  // An interactive install cannot reach this blocked, because Next is disabled on the page above.
  // It is carried anyway rather than gated on WizardSilent: a stop that exists in one of the two
  // paths is the kind that is discovered by the path nobody tested.
  Result := '';
  if Preflight then
  begin
    // The other half of DD236, and the only half a silent install has: there is no ready page to
    // have stopped anything on. Free where the ready page already did it, and the machine that
    // most needs it is the unattended one, where an in-use file is a deployment that fails with
    // nothing watching.
    StopBeforeInstalling;
    Exit;
  end;

  Result := 'This machine cannot host the container engine yet, so nothing was installed.'
          + #13#10#13#10 + PreflightSaid + #13#10 + PreflightReport;
end;

// ---------------------------------------------------------------------------------------------
// The engine itself (DD119)
// ---------------------------------------------------------------------------------------------

procedure ProvisionSaid(const S: String; const Error, FirstLine: Boolean);
var
  Line: string;
begin
  Line := Trim(S);
  if Line = '' then
    Exit;

  // Kept whatever happens, and beside the preflight report for the same reason: this is the file
  // somebody opens after Setup has closed, so {tmp} — which Setup deletes on its way out — is the
  // one place it must not be.
  SaveStringToFile(ProvisionLogPath, Line + #13#10, True);

  // `--provision` prints exactly one of these per step, pass or fail, and nothing else it writes
  // opens with either. Counting them is what moves the bar.
  if (Pos('[ok  ]', Line) = 1) or (Pos('[FAIL]', Line) = 1) then
  begin
    ProvisionStepsSeen := ProvisionStepsSeen + 1;
    ProvisionPage.SetProgress(ProvisionStepsSeen, ProvisioningSteps);
    ProvisionLastLine := Line;
  end;

  // The step line goes on the page, so a run that stops leaves the step it stopped at on screen
  // rather than a bar that has already moved past it.
  ProvisionPage.SetText(
    'Downloading and installing the engine. This can take several minutes.', ProvisionLastLine);
end;

function ProvisionEngine: Boolean;
var
  Code: Integer;
  Ran: Boolean;
begin
  ProvisionStepsSeen := 0;
  ProvisionLastLine := '';
  ProvisionLogPath := ExpandConstant('{app}\provision.log');

  // Truncated rather than appended to: a reinstall's log is about this run, and two runs in one
  // file is a reader guessing which failure is the live one.
  DeleteFile(ProvisionLogPath);

  ProvisionPage.SetText('Contacting dl-cdn.alpinelinux.org and download.docker.com.', '');
  ProvisionPage.SetProgress(0, ProvisioningSteps);
  ProvisionPage.Show;
  try
    // ExecAndLogOutput and not Exec: the child's output is what fills the page above, a line at a
    // time as each step lands. The working directory is {app} so a relative path in any error the
    // verb prints resolves where the reader would look for it.
    Ran := ExecAndLogOutput(
      ExpandConstant('{app}\{#MyAppExeName}'), '--provision', ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, Code, @ProvisionSaid);
  finally
    ProvisionPage.Hide;
  end;

  Result := Ran and (Code = 0);
  if Result or WizardSilent then
    Exit;

  if MsgBox('FreeWilly is installed, but the engine is not.' + #13#10#13#10
          + 'This machine passed the preflight, so the download or the WSL2 import is what '
          + 'stopped — a connection that dropped, a proxy, or a checksum that did not match. '
          + 'Nothing is half-installed: every step is repeatable, and what is already verified '
          + 'on disk is not fetched again.' + #13#10#13#10
          + 'To try again, open a terminal and run:' + #13#10
          + '    freewilly --provision' + #13#10#13#10
          + 'The step it stopped at is the last line of:' + #13#10
          + ProvisionLogPath + #13#10#13#10
          + 'Open it now?',
            mbError, MB_YESNO) = IDYES then
    ShellExec('open', ProvisionLogPath, '', '', SW_SHOWNORMAL, ewNoWait, Code);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // DD141 moved the vendor's CLI out of the directory on PATH, and an install made before that has
  // it sitting exactly where the forwarder is about to be written. Moved rather than overwritten:
  // the file about to land there is 13 MB of forwarder, and leaving the user to re-download a CLI
  // this machine already verified is a provision somebody has to be told to run.
  //
  // ssInstall, because it is the one step that runs before a single file is copied. Silent when
  // there is nothing to move, which is every fresh install and every upgrade made after this.
  if CurStep = ssInstall then
  begin
    if FileExists(ExpandConstant('{app}\bin\docker.exe'))
       and not FileExists(ExpandConstant('{app}\cli\docker.exe')) then
    begin
      CreateDir(ExpandConstant('{app}\cli'));
      RenameFile(
        ExpandConstant('{app}\bin\docker.exe'), ExpandConstant('{app}\cli\docker.exe'));
    end;

    Exit;
  end;

  if CurStep <> ssPostInstall then
    Exit;

  // The order is the argument, and DD130 moved where it is settled: the preflight ran before the
  // first file was written, so reaching this line at all means the machine cleared it. The guard
  // stays because the property it states is the one that matters — an engine is never unpacked
  // onto a machine that cannot host one — and `Preflight` remembers its answer, so restating it
  // here costs nothing and cannot disagree with the page that showed it.
  if not Preflight then
    Exit;

  // DD146. The reading this install was cleared on, kept where somebody would look for it. The
  // write in `Preflight` cannot land here: on a fresh install it runs before a single file exists,
  // so `{app}` is not there to write into and the report goes to TEMP — which is right for a
  // blocked install and useless for one that went through.
  //
  // Not re-read. This is the report the wizard acted on, not a second opinion about the machine a
  // moment later, and the two would differ on exactly the row the provision below is about to
  // change.
  KeepTheReport(ExpandConstant('{app}\preflight.txt'));

  if WizardIsTaskSelected('engine') then
    ProvisionEngine;
end;

// ---------------------------------------------------------------------------------------------
// Uninstall: stop what is running before deleting it (DD121)
// ---------------------------------------------------------------------------------------------
//
// An uninstall that cannot delete the program it is uninstalling is not an uninstall. The tray holds
// {app}\FreeWilly.exe open, so what used to happen was: the Run value went, the PATH entry went, the
// Add/Remove Programs entry went, and then the one file could not be deleted — leaving a root nobody
// owns and no uninstaller left to offer to take it.
//
// Three steps here, in this order: ask, stop, then delete and say what could not be removed rather
// than exiting 0 over it. The stop itself moved up the file under DD236, because the install turned
// out to need the same three verbs for the same reason.

var
  // What the page below decided, read again in usPostUninstall — after Inno has removed its own
  // files, which is the only point at which the root is otherwise empty. Silence means keep, so the
  // safe default is what an unattended uninstall gets by never touching this.
  RemoveTheDistribution: Boolean;

function OwnedDataExists: Boolean;
begin
  // The distro directory is where the imported virtual disk lives, so its presence is the question.
  // Deliberately not asked by running `wsl -l`: wsl.exe writes UTF-16LE, which is the one decoding
  // wart this project has already been bitten by, and a misread here would delete on a maybe.
  Result := DirExists(ExpandConstant('{app}\distro'))
         or DirExists(ExpandConstant('{app}\downloads'));
end;

/// Lay one wrapping paragraph out at Y and answer the Y the next control starts at.
function Paragraph(Form: TSetupForm; const Text: string; Left, Top, Width: Integer): Integer;
var
  Block: TLabel;
begin
  Block := TLabel.Create(Form);
  Block.Parent := Form;

  // AutoSize with WordWrap is what makes the height follow the text rather than the other way
  // round. A fixed height would be a guess about a font this script does not choose and a language
  // it does not know: BrazilianPortuguese.isl is shipped beside Default.isl, and the first
  // translation of this page that runs one line longer would have that line clipped off the bottom
  // of a fixed box with nothing to say it had happened.
  // The order is the whole of it, and both ways of getting it wrong were measured on this page.
  //
  // An auto-sizing label re-measures itself when its CAPTION changes and not when its width does,
  // and it measures at whatever width it has at that moment. So the width has to be right before
  // the text arrives, and these two flags have to be set before the width — an auto-sizing label
  // with no text in it collapses to nothing, and a flag flipped after the width would collapse a
  // width that had already been set.
  //
  // Setting the flags first, then the width, then the caption is the one order that leaves both
  // the wrap column and the height correct. Caption before width wraps the text at a column of
  // zero: one word per line, a page tall, and a form dragged out to the size of the screen. Caption
  // before width but width set afterwards is worse, because it looks fixed — the text re-wraps to
  // the right column, and the height stays at the value the collapsed measurement produced, so the
  // page renders correctly above a screenful of blank space.
  Block.WordWrap := True;
  Block.AutoSize := True;
  Block.Left := Left;
  Block.Top := Top;
  Block.Width := Width;
  Block.Caption := Text;

  Result := Block.Top + Block.Height;
end;

function AskAboutTheUninstall: Boolean;
var
  Form: TSetupForm;
  Heading: TLabel;
  Distribution: TNewCheckBox;
  Proceed, Abandon: TNewButton;
  Left, Width, Y, Buttons: Integer;
begin
  // Setup's wizard-page API is Setup's alone, so an uninstaller that wants a page builds the form.
  // It is worth the lines: the two things about to happen — a running program closed, and possibly
  // every image and volume deleted — are precisely the two a MsgBox chain asks about one at a time,
  // out of order, with no way to see them together before agreeing to either.
  //
  // Fixed width and not resizable: the prose is written to a column, and a form somebody could drag
  // wider would only re-wrap it. The height is set at the end, from what the text actually measured.
  Left := ScaleX(16);
  Width := ScaleX(398);

  Form := CreateCustomForm(Width + (2 * Left), ScaleY(260), False, False);
  try
    Form.Caption := 'Uninstall {#MyAppName}';

    Heading := TLabel.Create(Form);
    Heading.Parent := Form;
    Heading.Left := Left;
    Heading.Top := ScaleY(16);
    Heading.AutoSize := True;
    Heading.Font.Style := [fsBold];
    Heading.Caption := 'These are closed before anything is removed';
    Y := Heading.Top + Heading.Height + ScaleY(10);

    Y := Paragraph(Form,
        'The tray icon and window, asked to close themselves.'#13#10
      + 'The container engine, and the ' + DistroName + ' distribution it runs in.'#13#10#13#10
      + 'Anything still holding a file after that is closed forcibly. Windows will not delete a '
      + 'program that is running, and an uninstall that stops there leaves a folder nothing can '
      + 'remove.', Left, Y, Width) + ScaleY(16);

    Distribution := TNewCheckBox.Create(Form);
    Distribution.Parent := Form;
    Distribution.SetBounds(Left, Y, Width, ScaleY(17));
    Distribution.Caption := 'Also delete the WSL2 distribution';

    // Off, and it stays off in every path that does not include somebody reading this. Stopping is
    // reversible; this is the one question here with no undo.
    Distribution.Checked := False;
    Distribution.Enabled := OwnedDataExists;
    Y := Distribution.Top + Distribution.Height + ScaleY(4);

    // Indented under the box it qualifies, so the sentence with no undo in it is read as part of
    // the tick rather than as a second, separate thing.
    if OwnedDataExists then
      Y := Paragraph(Form,
          'It holds every image, container and volume FreeWilly created, and there is no undo. '
        + 'Left alone it stays on disk, and reinstalling FreeWilly picks it up again.'#13#10
        + ExpandConstant('{app}'), Left + ScaleX(16), Y, Width - ScaleX(16))
    else
      Y := Paragraph(Form,
          'There is none on this machine — nothing was ever provisioned here.',
          Left + ScaleX(16), Y, Width - ScaleX(16));

    Buttons := Y + ScaleY(20);

    // Positioned against the text column rather than against Form.ClientWidth, which is not the
    // width this page asked for until the assignment below has actually happened.
    Proceed := TNewButton.Create(Form);
    Proceed.Parent := Form;
    Proceed.SetBounds(Left + Width - ScaleX(75 + 6 + 75), Buttons, ScaleX(75), ScaleY(23));
    Proceed.Caption := 'Remove';
    Proceed.ModalResult := mrOk;
    Proceed.Default := True;

    Abandon := TNewButton.Create(Form);
    Abandon.Parent := Form;
    Abandon.SetBounds(Left + Width - ScaleX(75), Buttons, ScaleX(75), ScaleY(23));
    Abandon.Caption := 'Cancel';
    Abandon.ModalResult := mrCancel;
    Abandon.Cancel := True;

    // Now that everything has measured itself. A form sized before its text is a form with either
    // a clipped last line or a band of empty space under the buttons, depending on the machine.
    // The width is restated rather than trusted: it is the one number every control above was
    // placed against, so this is where the two are held equal.
    Form.ClientWidth := Width + (2 * Left);
    Form.ClientHeight := Buttons + ScaleY(23 + 16);
    Form.ActiveControl := Proceed;

    // Centred on the screen by CreateCustomForm itself. There is no WizardForm to centre on in an
    // uninstall, and the progress window behind this one is Inno's rather than a page of ours.
    Result := Form.ShowModal = mrOk;
    if Result then
      RemoveTheDistribution := Distribution.Checked;
  finally
    Form.Free;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  // Before Inno removes a single file, and after the user has already confirmed the uninstall
  // itself: the page below asks the two questions that confirmation could not, and Cancel on it
  // abandons the whole uninstall rather than leaving a half-removed install behind.
  if CurUninstallStep = usUninstall then
  begin
    // An unattended uninstall gets the safe half: everything is stopped, and nothing anybody owns
    // is deleted. A modal box in a deployment is a machine that looks hung to whoever pushed it.
    if not UninstallSilent then
    begin
      if not AskAboutTheUninstall then
        Abort;
    end;

    StopEverything;

    RemovePathEntry;

    // Everything this install put in {app} that Inno did not, and therefore does not know to take
    // back: the two reports, the CLI the provision extracted, and the plugin directory it filled.
    // None of it is anybody's data — it is this product's own files under this product's own root.
    // DD119 made that universal by provisioning during the install, so it is settled here rather
    // than left to the question above, which is about images and volumes and nothing else.
    DeleteFile(ExpandConstant('{app}\preflight.txt'));
    DeleteFile(ExpandConstant('{app}\provision.log'));

    // DD137's journal, on the same footing as the two above: this product's own account of its own
    // engine, under this product's own root. Left behind it is one more file keeping {app} on disk
    // after an uninstall that took everything else, which is the failure DD121 exists to remove.
    DeleteFile(ExpandConstant('{app}\engine.log'));

    // DD216's prepared rescue, on the same footing (DD223). It is a cache this product wrote from
    // the rootfs it pins, not anybody's data, so it goes with the uninstall rather than with the
    // question about images and volumes: a machine that removed the product must not keep eleven
    // megabytes it cannot account for. The wildcard takes the one this build names and any left by
    // an earlier one, since the file carries the rootfs digest and a bump makes a new name.
    DelTree(ExpandConstant('{app}\rescue-*.tar'), False, True, False);

    // What this install learned about the machine's sparse disks (DD226). A note this product wrote
    // about somebody's Windows, not a setting they chose, and a file left in {app} keeps the root on
    // disk after an uninstall that took everything else.
    DeleteFile(ExpandConstant('{app}\sparse-refused.txt'));

    DelTree(ExpandConstant('{app}\bin'), True, True, True);
    DelTree(ExpandConstant('{app}\cli'), True, True, True);
    DelTree(ExpandConstant('{app}\cli-plugins'), True, True, True);

    if RemoveTheDistribution then
    begin
      // Terminated again rather than trusting the stop above: this is the one operation here that
      // cannot be repeated, and the executable that would have run --stop may not have been there.
      Exec(ExpandConstant('{sys}\wsl.exe'), '--terminate ' + DistroName,
           '', SW_HIDE, ewWaitUntilTerminated, Code);

      // Unregister first: the virtual disk is open while the distribution is registered, so
      // deleting the directory underneath it fails and leaves a distribution pointing at nothing.
      Exec(ExpandConstant('{sys}\wsl.exe'), '--unregister ' + DistroName,
           '', SW_HIDE, ewWaitUntilTerminated, Code);
      DelTree(ExpandConstant('{app}\distro'), True, True, True);
      DelTree(ExpandConstant('{app}\downloads'), True, True, True);
    end;

    Exit;
  end;

  if CurUninstallStep <> usPostUninstall then
    Exit;

  // Everything above happens in usUninstall, and that placement is the whole of this paragraph.
  //
  // Inno removes the install directory during its own file pass, which runs between usUninstall and
  // usPostUninstall, and it removes it only if it is empty by then. Doing this work in
  // usPostUninstall — where it stood — meant Inno met a root still holding distro\ and downloads\,
  // left it alone as it should, and then this emptied it a moment too late. Measured: an uninstall
  // that removed the registration, the distribution, the virtual disk and every file, and left an
  // empty C:\Users\...\FreeWilly standing with nothing left that would ever offer to take it.
  //
  // So the only thing left for this step is to check the work, and it cannot check the root: the
  // uninstaller is still running out of it, unins000.exe is still in it, and Inno deletes both
  // after this returns. What it can check is what the page promised — that the distribution and its
  // downloads are gone — and that is the promise with no undo behind it.
  if UninstallSilent or not RemoveTheDistribution or not OwnedDataExists then
    Exit;

  MsgBox('FreeWilly is uninstalled, but part of the WSL2 distribution could not be deleted:'
       + #13#10#13#10
       + ExpandConstant('{app}') + #13#10#13#10
       + 'Something was still holding a file inside it — most often WSL2 itself, which keeps the '
       + 'virtual disk open for a while after a distribution is unregistered. Running '
       + '"wsl --shutdown" and then deleting the folder by hand finishes it. Nothing here is '
       + 'registered any more, and nothing will start it again.',
         mbInformation, MB_OK);
end;
