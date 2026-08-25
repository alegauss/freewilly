// The illustrative SVGs. These are figures — the pipe's topology and dark screenshots of
// an app that is itself dark on Windows — so they keep their own fixed palette rather than
// following the page theme; the themed .shot-frame around them is what places them on a
// light or dark page. Kept as verbatim markup (not converted to JSX) so the drawing stays
// pixel-identical to the hand-written original, and rendered with dangerouslySetInnerHTML
// because it is static, author-controlled content with no interpolation.

export const pipeDiagram = `
<svg viewBox="0 0 900 250" role="img" aria-label="docker.exe, compose and the FreeWilly window all connect to the Windows named pipe docker_engine, which the relay forwards over wsl.exe stdio to socat inside the freewilly distribution, and on to the dockerd unix socket">
  <defs>
    <marker id="arw" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="#67C3F2"/>
    </marker>
  </defs>
  <rect width="900" height="250" rx="12" fill="#0a151d"/>

  <text x="30" y="34" fill="#7d95a5" font-family="Inter,sans-serif" font-size="12" font-weight="700" letter-spacing="1.4">WINDOWS</text>
  <line x1="470" y1="16" x2="470" y2="234" stroke="#22404f" stroke-width="1" stroke-dasharray="5 5"/>
  <text x="500" y="34" fill="#7d95a5" font-family="Inter,sans-serif" font-size="12" font-weight="700" letter-spacing="1.4">WSL2 · freewilly</text>

  <rect x="30" y="54" width="150" height="34" rx="8" fill="#11212c" stroke="#22404f"/>
  <text x="105" y="76" text-anchor="middle" fill="#e9f2f8" font-family="JetBrains Mono,monospace" font-size="13">docker.exe</text>
  <rect x="30" y="100" width="150" height="34" rx="8" fill="#11212c" stroke="#22404f"/>
  <text x="105" y="122" text-anchor="middle" fill="#e9f2f8" font-family="JetBrains Mono,monospace" font-size="13">docker compose</text>
  <rect x="30" y="146" width="150" height="34" rx="8" fill="#11212c" stroke="#22404f"/>
  <text x="105" y="168" text-anchor="middle" fill="#e9f2f8" font-family="Inter,sans-serif" font-size="11.5">the FreeWilly window</text>

  <path d="M186 71 H250" stroke="#67C3F2" stroke-width="1.6" fill="none" marker-end="url(#arw)"/>
  <path d="M186 117 H250" stroke="#67C3F2" stroke-width="1.6" fill="none" marker-end="url(#arw)"/>
  <path d="M186 163 H250" stroke="#67C3F2" stroke-width="1.6" fill="none" marker-end="url(#arw)"/>

  <rect x="256" y="54" width="196" height="126" rx="10" fill="#132734" stroke="#2E9BD6" stroke-width="1.6"/>
  <text x="354" y="88" text-anchor="middle" fill="#67C3F2" font-family="JetBrains Mono,monospace" font-size="12">\\\\.\\pipe\\</text>
  <text x="354" y="106" text-anchor="middle" fill="#67C3F2" font-family="JetBrains Mono,monospace" font-size="13" font-weight="500">docker_engine</text>
  <text x="354" y="132" text-anchor="middle" fill="#9db3c1" font-family="Inter,sans-serif" font-size="11.5">the relay, in the tray's</text>
  <text x="354" y="148" text-anchor="middle" fill="#9db3c1" font-family="Inter,sans-serif" font-size="11.5">own process</text>
  <text x="354" y="168" text-anchor="middle" fill="#2EA043" font-family="Inter,sans-serif" font-size="11.5" font-weight="600">ACL: your account only</text>

  <path d="M458 117 H556" stroke="#67C3F2" stroke-width="1.6" fill="none" marker-end="url(#arw)"/>
  <text x="507" y="97" text-anchor="middle" fill="#7d95a5" font-family="Inter,sans-serif" font-size="11">wsl.exe</text>
  <text x="507" y="111" text-anchor="middle" fill="#7d95a5" font-family="Inter,sans-serif" font-size="11">stdio</text>

  <rect x="562" y="98" width="106" height="38" rx="8" fill="#11212c" stroke="#22404f"/>
  <text x="615" y="122" text-anchor="middle" fill="#e9f2f8" font-family="JetBrains Mono,monospace" font-size="13">socat</text>
  <path d="M674 117 H736" stroke="#67C3F2" stroke-width="1.6" fill="none" marker-end="url(#arw)"/>

  <rect x="742" y="90" width="128" height="54" rx="8" fill="#11212c" stroke="#2EA043" stroke-width="1.4"/>
  <text x="806" y="112" text-anchor="middle" fill="#e9f2f8" font-family="JetBrains Mono,monospace" font-size="13">dockerd</text>
  <text x="806" y="130" text-anchor="middle" fill="#9db3c1" font-family="JetBrains Mono,monospace" font-size="10.5">/var/run/docker.sock</text>

  <text x="450" y="216" text-anchor="middle" fill="#7d95a5" font-family="Inter,sans-serif" font-size="11.5">No forwarded TCP port anywhere on this path: the Engine API is equivalent to root on the machine,</text>
  <text x="450" y="232" text-anchor="middle" fill="#7d95a5" font-family="Inter,sans-serif" font-size="11.5">and a port every local process can reach cannot express “only me”.</text>
</svg>`;

// DD160. The menu, drawn from what TrayMenu actually builds — the window first and alone above
// its rule, the two engine verbs, the setting that qualifies them, and the way out. It drew
// three items under a heading claiming four, in the order the menu had before DD140 moved the
// window to the front, and the section's bullets are held to this same source now.
//
// The captions are typed here rather than read from the generated module, and only here: this
// is markup, not copy, and a text node whose x/y are hand-placed cannot take a string of
// unknown width. What keeps it honest is product.test.mjs, which asserts every caption the
// menu shows appears in this drawing — a renamed item fails the build rather than leaving a
// picture of a menu nobody ships.
//
// Start engine is the greyed one: the icon below it is the filled disc, so the engine is
// running and the item that starts one is exactly what would be disabled.
export const trayMenuDiagram = `
<svg viewBox="0 0 420 300" role="img" aria-label="The tray icon showing a filled green disc, with its context menu: Open window, Start engine (greyed out because the engine is running), Stop engine, Start engine with FreeWilly, Quit">
  <rect width="420" height="300" rx="12" fill="#0a151d"/>
  <rect x="52" y="44" width="266" height="178" rx="10" fill="#172c39" stroke="#22404f"/>
  <text x="72" y="70" fill="#e9f2f8" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">Open window</text>
  <line x1="64" y1="86" x2="306" y2="86" stroke="#22404f"/>
  <text x="72" y="110" fill="#7d95a5" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">Start engine</text>
  <text x="72" y="136" fill="#e9f2f8" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">Stop engine</text>
  <path d="M74 156 l4 4 l7 -8" stroke="#67C3F2" stroke-width="1.8" fill="none" stroke-linecap="round" stroke-linejoin="round"/>
  <text x="92" y="161" fill="#e9f2f8" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">Start engine with FreeWilly</text>
  <line x1="64" y1="178" x2="306" y2="178" stroke="#22404f"/>
  <text x="72" y="202" fill="#e9f2f8" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">Quit</text>
  <rect x="0" y="240" width="420" height="60" fill="#101c25"/>
  <line x1="0" y1="240" x2="420" y2="240" stroke="#22404f"/>
  <circle cx="176" cy="270" r="9" fill="#2EA043"/>
  <circle cx="212" cy="270" r="8" fill="none" stroke="#3c5666" stroke-width="2.5"/>
  <circle cx="248" cy="270" r="8" fill="none" stroke="#3c5666" stroke-width="2.5"/>
  <text x="300" y="275" fill="#7d95a5" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="12">14:08</text>
  <rect x="96" y="220" width="160" height="22" rx="4" fill="#1b2f3c" stroke="#2a4655"/>
  <text x="176" y="235" text-anchor="middle" fill="#c8dbe6" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="11">FreeWilly — engine running</text>
</svg>`;

export const windowDiagram = `
<svg viewBox="0 0 900 420" role="img" aria-label="The FreeWilly window: engine running, then a table of containers with name, image, state, status and clickable published ports">
  <rect width="900" height="420" rx="12" fill="#0e1a22"/>
  <rect x="0" y="0" width="900" height="42" rx="12" fill="#132029"/>
  <rect x="0" y="30" width="900" height="12" fill="#132029"/>
  <text x="24" y="27" fill="#e9f2f8" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">FreeWilly</text>
  <text x="820" y="27" fill="#7d95a5" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="14" letter-spacing="6">─☐✕</text>

  <circle cx="34" cy="76" r="6" fill="#2EA043"/>
  <text x="50" y="81" fill="#e9f2f8" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="15">Engine running</text>
  <text x="176" y="81" fill="#7d95a5" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">· Engine API v1.43</text>

  <text x="36" y="120" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="11" font-weight="600" letter-spacing="1">NAME</text>
  <text x="200" y="120" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="11" font-weight="600" letter-spacing="1">IMAGE</text>
  <text x="404" y="120" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="11" font-weight="600" letter-spacing="1">STATE</text>
  <text x="500" y="120" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="11" font-weight="600" letter-spacing="1">STATUS</text>
  <text x="676" y="120" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="11" font-weight="600" letter-spacing="1">PORTS</text>

  <rect x="24" y="134" width="852" height="46" rx="6" fill="#152530"/>
  <text x="36" y="162" fill="#e9f2f8" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13.5">web</text>
  <text x="200" y="162" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">nginx:1.29-alpine</text>
  <text x="404" y="162" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">running</text>
  <text x="500" y="162" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">Up 12 seconds</text>
  <rect x="670" y="146" width="104" height="22" rx="4" fill="#1d3a4b"/>
  <text x="680" y="162" fill="#67C3F2" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="12.5">8080-&gt;80/tcp</text>

  <text x="36" y="212" fill="#e9f2f8" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13.5">postgres-dev</text>
  <text x="200" y="212" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">postgres:17-alpine</text>
  <text x="404" y="212" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">running</text>
  <text x="500" y="212" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">Up 4 minutes</text>
  <text x="680" y="212" fill="#67C3F2" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="12.5">5432-&gt;5432/tcp</text>

  <text x="36" y="262" fill="#e9f2f8" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13.5">redis</text>
  <text x="200" y="262" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">redis:8-alpine</text>
  <text x="404" y="262" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">running</text>
  <text x="500" y="262" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">Up 4 minutes</text>
  <text x="680" y="262" fill="#7d95a5" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="12.5">6379/tcp</text>

  <text x="36" y="312" fill="#e9f2f8" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13.5">api</text>
  <text x="200" y="312" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">node:22-alpine</text>
  <text x="404" y="312" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">exited</text>
  <text x="500" y="312" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">Exited (1) 3 minutes ago</text>
  <text x="680" y="312" fill="#3c5666" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="12.5">—</text>

  <line x1="24" y1="352" x2="876" y2="352" stroke="#1b2e3a"/>
  <text x="36" y="382" fill="#7d95a5" font-family="Inter,sans-serif" font-size="11.5">A blue port is published and opens in your browser. Grey is exposed only, and there is nowhere on this machine to send you,</text>
  <text x="36" y="398" fill="#7d95a5" font-family="Inter,sans-serif" font-size="11.5">so it is plain text and not a link that lands on nothing.</text>
</svg>`;

export const emptyStateDiagram = `
<svg viewBox="0 0 420 280" role="img" aria-label="The empty state: the engine is not running, with a Start the engine button">
  <rect width="420" height="280" rx="12" fill="#0e1a22"/>
  <rect x="0" y="0" width="420" height="36" rx="12" fill="#132029"/>
  <rect x="0" y="24" width="420" height="12" fill="#132029"/>
  <text x="18" y="23" fill="#e9f2f8" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="12">FreeWilly</text>
  <circle cx="26" cy="64" r="5" fill="#8B949E"/>
  <text x="38" y="68" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">Engine stopped</text>
  <text x="210" y="150" text-anchor="middle" fill="#e9f2f8" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="19">The engine is not running</text>
  <text x="210" y="176" text-anchor="middle" fill="#9db3c1" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="12.5">Start it to see your containers.</text>
  <rect x="145" y="200" width="130" height="36" rx="6" fill="#1d3a4b" stroke="#2E9BD6"/>
  <text x="210" y="223" text-anchor="middle" fill="#e9f2f8" font-family="Segoe UI Variable Text,Segoe UI,Inter,sans-serif" font-size="13">Start the engine</text>
</svg>`;

// The preflight's terminal output — a formatted figure like the SVGs, kept verbatim so
// the column alignment and the per-cell status colours render exactly as the hand-written
// original. The claims it carries (the build number, the remedy command) stay greppable
// in this source. Rendered inside a .term <pre>.
export const preflightTerminal = `FreeWilly preflight — what this machine can host

  [<span class="ok">ok  </span>]  Windows build            Windows 10.0, build 26200
  [<span class="ok">ok  </span>]  Hardware virtualization  enabled — a hypervisor is already running
  [<span class="fail">FAIL</span>]  WSL2                     not installed — wsl.exe is not on this machine
           <span class="rem">-> Run \`wsl --install --no-distribution\` in an administrator terminal, then
              reboot.</span>
  [<span class="ok">ok  </span>]  Container engine         nothing else owns the docker command or the docker_engine pipe

<span class="sum">1 row blocks an install. Nothing has been copied to disk.</span>`;

// The three engine-state marks, exactly the hex the tray's StateIcon draws (§1). Small
// enough to inline as JSX in the Tray section.
export const stateIcons = {
  run: `<svg viewBox="0 0 48 48" aria-hidden="true"><circle cx="24" cy="24" r="21" fill="#2EA043"/></svg>`,
  start: `<svg viewBox="0 0 48 48" aria-hidden="true"><path d="M38.85 9.15 A21 21 0 1 1 9.15 9.15" fill="none" stroke="#D29A00" stroke-width="6"/></svg>`,
  stop: `<svg viewBox="0 0 48 48" aria-hidden="true"><circle cx="24" cy="24" r="21" fill="none" stroke="#8B949E" stroke-width="6"/></svg>`,
};
