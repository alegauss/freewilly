// The copy lives here and nowhere else (S3). Every section component imports a value
// from this module and only renders it — so a claim is an array element a reviewer can
// check against the product, not a string welded into the markup that displays it. The
// composition (which section, in which order, and the illustrative SVGs) lives in the
// JSX; this file is the words.
//
// Fragments carrying inline code or emphasis are modelled as a small tagged run list
// (`Rich`) rather than raw HTML, so a section renders them without dangerouslySetInnerHTML
// and the twin generator in DD42 has a structure to convert rather than markup to parse.

// DD159. Every count in the prose below comes from here, and none of it is typed. The copy
// states the reason and the generator states the number: five of the eight drifts DD157
// corrected were counts nobody could have kept, each true when it was typed and none with a
// gate behind it. Prose is still prose — "the ports are links" is a sentence a reviewer reads.
import {
  Spelled,
  acquireCount,
  artefactCount,
  destinationCount,
  helpExcerpt,
  menuCount,
  product,
  rowCount,
  spelled,
  stepCount,
  version,
} from "./product";

export type Run =
  | string
  | { code: string }
  | { b: string }
  | { i: string };

export type Rich = Run[];

/**
 * A list of hosts as prose: `a`, `b` and `c`, each marked as code (DD159).
 *
 * The hosts come off the manifest, so their number is not fixed — and a sentence built by
 * joining them with commas would read "a, b, github.com" the day a fourth appears. The
 * conjunction is the part a generated list cannot supply for itself.
 */
function hostRuns(hosts: readonly string[]): Rich {
  return hosts.flatMap((host, i) => {
    const separator = i === 0 ? [] : [i === hosts.length - 1 ? " and " : ", "];
    return [...separator, { code: host }];
  });
}

/* ------------------------------------------------------------------ meta + chrome */

export const meta = {
  title: "FreeWilly: Docker on Windows, without Docker Desktop",
  description:
    "A free Windows desktop app that installs upstream Moby into a WSL2 distribution it owns, serves the docker_engine pipe your existing tools already look for, and shows your containers in one window. Per-user install, no admin, nothing resident.",
  og: {
    title: "FreeWilly",
    description:
      "Upstream Moby in a WSL2 distro it owns, the docker_engine pipe your tools already look for, and one window of containers. No licence fee, nothing resident.",
    url: "https://alegauss.github.io/freewilly/",
  },
} as const;

export const repoUrl = "https://github.com/alegauss/freewilly";
export const parentUrl = "https://alegauss.github.io/";

// The release page rather than a file: the installer carries its version in its name, so
// there is no version-independent URL for the asset itself, and a hard-coded
// `FreeWilly-Setup-1.0.0.exe` would 404 on the day 1.0.1 ships. `releases/latest` is the
// one link that cannot go stale, and it is also where the checksum lives.
export const releasesUrl = `${repoUrl}/releases/latest`;

// Section anchors (#x) act on the landing page; the page links are base-absolute so they
// resolve the same from every route. The brand and footer link home the same way.
export const navLinks = [
  { href: "#operator", label: "Agent" },
  { href: "#pipe", label: "The pipe" },
  { href: "#window", label: "Window" },
  { href: "/freewilly/claude-code/", label: "Claude Code" },
  { href: "/freewilly/compare/", label: "Compare" },
  // No "Download" link here, and DD155 is why: it sat immediately left of the download
  // button, so the row carried the same destination twice and paid for it in width — five
  // labels, two buttons and the theme toggle wrapped "The pipe" and "Claude Code" onto two
  // lines each. The button is the affordance; a link repeating it is what got dropped.
] as const;

export const footer = {
  links: [
    { href: "/freewilly/claude-code/", label: "Claude Code" },
    { href: "/freewilly/compare/", label: "Compare" },
      { href: repoUrl, label: "GitHub" },
    { href: releasesUrl, label: "Releases" },
    { href: `${repoUrl}/blob/main/docs/ROADMAP.md`, label: "Roadmap" },
    { href: `${repoUrl}/blob/main/CONTRIBUTING.md`, label: "Contributing" },
  ],
  // The trademark disclaimer is not up for revision (§1). DD13 shipped LICENSE and NOTICE,
  // so the claim is now the shape — a link to LICENSE — rather than the string "not written
  // yet", which is exactly the S1 corollary that survived Shio's 195-file licence change.
  disclaimer:
    "Unofficial / community project. Not affiliated with, endorsed by, or sponsored by Docker, Inc. “Docker” and “Docker Desktop” are trademarks of Docker, Inc.; this tool installs the Apache-2.0 licensed Moby engine and Docker CLI as published upstream, unmodified, and pins each artefact to a version and a digest. Apache-2.0 is this project's stated licence, in LICENSE, with a NOTICE covering the bundled upstream binaries. © 2026 Alexandre Oliveira.",
} as const;

/* --------------------------------------------------------------- sponsor */

// Mirrors alegauss.github.io/sponsor.json — the canonical sponsor declaration for
// these projects. Transcribed here rather than fetched at runtime: this site is
// prerendered, and the whole point of naming a sponsor is that crawlers and LLMs
// read it in the served HTML.
export const sponsor = {
  label: "Sponsored by",
  name: "Viglet",
  url: "https://www.viglet.org",
  siteLabel: "viglet.org",
  logo: "/freewilly/viglet/viglet-logo.png",
  summary:
    "Open source search and content tools for organisations with a lot to publish. Run on your own servers, with no per-user licence.",
  products: [
    {
      name: "Viglet Turing ES",
      url: "https://turing.viglet.org",
      logo: "/freewilly/viglet/turing-logo.png",
      inline:
        "so visitors find what they came for, with AI answers drawn only from your own content",
    },
    {
      name: "Viglet Shio CMS",
      url: "https://shio.viglet.org",
      logo: "/freewilly/viglet/shio-logo.png",
      inline:
        "so a new page goes live the same day, reviewed and approved by your own team",
    },
  ],
} as const;

/* ------------------------------------------------------------------ hero */

export const hero = {
  badge: "Windows 10 / 11 · Per-user install · Apache-2.0",
  titleLead: "Docker on Windows,",
  titleAccent: "without Docker Desktop.",
  sub: [
    "FreeWilly puts ",
    { b: "upstream Moby" },
    " into a WSL2 distribution it owns, serves the ",
    { code: "docker_engine" },
    " pipe every tool you already have looks for, and shows your containers in one window. Per-user install, no administrator prompt, and nothing left running that you did not ask for.",
  ] as Rich,
  // No emoji on these three, and that is a writing rule rather than a taste: an emoji glued
  // to the front of a feature line is the most recognisable mannerism of a generated landing
  // page, and these strings are also bullets in the Markdown twin an agent reads (S5). The
  // card icons below sit in an icon slot of their own; these were decoration on prose.
  meta: [
    "Free at any headcount",
    "No telemetry, no account",
    "Not a background service",
  ],
  pills: [
    [{ b: ".NET 10" }, " · WinForms tray + WPF window"] as Rich,
    ["Upstream ", { b: `Moby ${version("engine")}` }, ", pinned by digest"] as Rich,
    ["One owned ", { b: "WSL2" }, " distro"] as Rich,
    ["Zero NuGet ", { b: "Engine API client" }] as Rich,
  ],
};

/* ------------------------------------------------------------------ hero session */
// The five-call session and the context pack from DD23's own acceptance criteria. The
// commands and their flags are the real agent surface; the values are illustrative demo
// data, and every cost here is a target the benchmark must prove or falsify rather than a
// measurement — which is why they are typed and the friction section's are not. That split
// is the constitution's law on generated figures: a measured cost is generated or it is not
// on the page, and an unmeasured one is stated as the criterion it is.
// Rendered as an autoplaying transcript that scrolls its own list (S7).

export const heroSession = {
  eyebrow: "What it costs an agent",
  question:
    "bring this project's stack up and tell me why the api container is not responding",
  steps: [
    {
      cmd: "freewilly read context",
      cost: "~150 tok",
      // the context pack (§4.2): one budgeted payload in a terse line format, not JSON
      pack: [
        "engine  running  wsl:freewilly  api=v1.43  pipe=docker_engine  ctx=default(ok)",
        "web     up 6m       healthy   svc:shop/web   :8080→80   listening",
        "db      up 6m       healthy   svc:shop/db    :5432→5432 listening",
        "api     exited 137  ×3/2m     svc:shop/api   OOM  limit=512m",
        "disk    images 14G (4.2G dangling)  volumes 2.1G (1 unused)",
        "compose ./docker-compose.yaml → shop  3 svc, 3 present",
        "cursor  c:4f21a0",
      ],
    },
    {
      cmd: "freewilly read doctor api",
      cost: "~200 tok",
      out: "api  OOM-killed ×3  ·  mem_limit 512m too low  →  raise it in compose",
    },
    {
      cmd: "freewilly read logs api --dedup --budget 1500 --out .freewilly/logs/api.log",
      cost: "~300 tok",
      out: "1 unique line ×3 (deduped from 41), written to file, then Grep, paying for matches only",
    },
    {
      cmd: "freewilly do compose up --wait",
      cost: "~100 tok",
      out: "shop  3/3 ready  ·  api healthy in 4.2s  (this one still asks, because it writes)",
    },
    {
      cmd: "freewilly read verify svc:shop/api",
      cost: "~80 tok",
      out: ":8080 answers from Windows  ·  200 OK  ·  PASS",
    },
  ],
  today: "Docker today: 15–30 calls · 30–60k tokens · 1–3 interruptions",
  target: "This session: ≈5 calls · ~2–5k tokens · 0 interruptions",
  note: [
    "A scripted session. The costs are ",
    { b: "targets the benchmark must prove or falsify" },
    ", acceptance criteria and not measurements. Steps 1, 2, 3 and 5 are reads, so one allowlist line, ",
    { code: "Bash(freewilly read:*)" },
    ", removes every prompt on the inspection path while step 4 still asks.",
  ] as Rich,
};

/* ------------------------------------------------------------------ operator: two actors + ten laws */
// DD23's constitution, restated for the site (§DD45). This is the description the project
// ships under and the reason its surface is shaped as it is — the largest thing the
// pre-DD23 page was out of date about. Each law names the defect it prevents, which is
// what makes it checkable rather than a value statement.

export const operator = {
  eyebrow: "Who operates this",
  heading: "The operator is an agent. You install and approve.",
  intro: [
    "FreeWilly is a Docker installation whose primary operator is a coding agent. The agent runs, inspects and diagnoses; you install, approve and intervene. Every decision on the agent surface is judged in tokens rather than clicks, which is why the site opens on a session and not a screenshot.",
  ] as Rich,
  actors: [
    {
      who: "Agent",
      sub: "Claude Code",
      iface: "the freewilly CLI, over an ordinary shell",
      job: "Run, inspect, diagnose, clean up after itself",
    },
    {
      who: "You",
      sub: "at the keyboard",
      iface: "the installer, the tray, the container and log windows",
      job: "Install, approve, intervene, uninstall",
    },
  ],
  actorsNote: [
    "The desktop path is not sacrificed: the installer, the tray and the window are what it is for, and they landed before the agent surface did rather than after.",
  ] as Rich,
  lawsEyebrow: "The design laws",
  lawsHeading: "Ten laws, in the order an agent meets them",
  lawsIntro: [
    "Binding, in the same sense as the product's. A feature that breaks one is wrong even if it was asked for. Each names the defect it prevents.",
  ] as Rich,
  laws: [
    { id: "P1", title: "The shell is the surface", body: "Every agent-facing capability is a freewilly CLI verb first. An MCP tool is a second head over the same method, or it does not exist." },
    { id: "P2", title: "One call replaces a session", body: "Learning what the machine is running is a product feature, not a docs problem. Needing six commands to learn the engine's state is a defect in FreeWilly." },
    { id: "P3", title: "Tokens are a measured budget", body: "Every response has a size ceiling and the canonical task has a measured cost. “It got cheaper” has to be a number, and a regression fails the build." },
    { id: "P4", title: "A file beats a stream", body: "An unbounded log read is the largest token sink here. Write it to disk and let the agent Grep it, so it pays for the lines that match, not for the whole log." },
    { id: "P5", title: "Names, not ids", body: "A 64-hex id changes on every recreate. The address is the name: svc:<project>/<service>, or the container name. Ids stop being currency threaded across calls." },
    { id: "P6", title: "Errors are instructions", body: "Every refusal carries what was wrong, what is allowed, the nearest match, a correct example, and the Windows fact that explains it. An error that costs a round trip to read is a defect." },
    { id: "P7", title: "Never surprise you", body: "Read and write are split at the argv level so an allowlist can tell them apart. Destructive calls take a confirm token, and everything the agent creates is labelled with its session." },
    { id: "P8", title: "The agent cannot see", body: "Give it cheap textual proof that what it started works (the port listens from Windows, the mount resolved, the service answered), or every mistake costs a trip back to you." },
    { id: "P9", title: "Session N+1 is cheaper than N", body: "A cursor and a change feed, so a follow-up session reads the delta rather than re-deriving the whole machine from nothing." },
    { id: "P10", title: "Compose, don't fork", body: "The surface is a shape over the Engine API and facts Windows already knows. It is not a second Docker CLI and never grows a build, a push or a compose up of its own." },
  ],
};

/* ------------------------------------------------------------------ why */

export const why = {
  eyebrow: "Why it exists",
  heading: "The engine is free. The desktop app is what costs.",
  intro: [
    "Moby and the ",
    { code: "docker" },
    " CLI are Apache-2.0 and always were. What a company pays for, and what a managed laptop cannot install, is the wrapper around them. FreeWilly is a different wrapper: it installs the same engine, gets out of the way, and stops when you tell it to.",
  ] as Rich,
  cards: [
    {
      icon: "🪪",
      title: "No licence to count seats against",
      body: [
        "Free at any headcount, for any use, with no seat maths and no renewal date. That is the whole reason to try this instead of the thing everyone already has.",
      ] as Rich,
    },
    {
      icon: "🔌",
      title: "Nothing resident",
      body: [
        "No Windows service, no scheduled task, and autostart is ",
        { b: "off" },
        " until you turn it on. The engine runs for exactly as long as something is running it. An engine that holds gigabytes from every boot is the complaint this project starts from.",
      ] as Rich,
    },
    {
      icon: "🧍",
      title: "Per-user, no admin prompt",
      body: [
        "Everything lands under ",
        { code: "%LOCALAPPDATA%\\FreeWilly" },
        ": the verified downloads, the distribution's disk, and the ",
        { code: "docker.exe" },
        " that goes on your PATH. Which is what reaches a corporate laptop you are not an administrator on.",
      ] as Rich,
    },
    {
      icon: "📦",
      title: "Upstream, not a fork",
      body: [
        "Alpine's minirootfs, Docker's own static Linux binaries, Docker's own Windows CLI, each pinned to a version ",
        { b: "and a SHA-256 this project states itself" },
        ". Nothing is patched, so nothing here can be blamed for how the engine behaves.",
      ] as Rich,
    },
    {
      icon: "🧪",
      title: "It checks before it copies",
      body: [
        "A preflight reads the Windows build, virtualization, the WSL2 kernel and any rival engine, and refuses the install while a blocking row is not green. ",
        { b: "Nothing is written to disk while the answer is no." },
      ] as Rich,
    },
    {
      icon: "🗑️",
      title: "An uninstall that is one command",
      body: [
        "The distribution is called ",
        { code: "freewilly" },
        " and it is this tool's, never yours. Your own WSL distros are untouched, and removing the engine cannot take anything of yours with it.",
      ] as Rich,
    },
  ],
};

/* ------------------------------------------------------------------ preflight */

export const preflight = {
  eyebrow: "Before anything is installed",
  // DD159. The count is PreflightInspection.Rows', not a number typed here: DD157 found this
  // saying four where the product reported five, which is the one class of claim a page cannot
  // notice about itself.
  heading: `Why Docker will not run here, in ${spelled(rowCount())} rows`,
  intro: [
    `“It does not work on my machine” has ${spelled(rowCount())} common causes on Windows, and they have ${spelled(rowCount())} different remedies. `,
    { code: "freewilly --preflight" },
    " names the one you have, prints the command that fixes it, changes nothing, and exits ",
    { code: "1" },
    " so an installer can stop rather than fail halfway.",
  ] as Rich,
  terminalTitle: "freewilly --preflight",
  // Split from the notes below it so the row count is assertable (DD159): a list mixing the
  // rows with what is true of every row cannot be held to the number the heading states, and
  // holding it to that number is the whole of why the heading no longer types one.
  rows: [
    ["Windows build", ": 19041 or later, because below it no amount of configuration gets a WSL2 kernel"],
    [
      "Hardware virtualization",
      ": and it reads the hypervisor first: Windows reports the firmware bit as off once something has claimed it, so the naive order sends you into a BIOS to enable what is already on",
    ],
    [
      "WSL2",
      ": a missing wsl.exe, a half-installed feature with no kernel behind it, and “new distros default to WSL1” are three different states with three different lines",
    ],
    [
      "Container engine",
      ": anything else that owns the docker command or the docker_engine pipe, because two engines competing for one pipe leaves neither working",
    ],
    [
      "Docker context",
      ": where your own docker command points, because an engine that is running and a client aimed at something else fail with the same sentence, and only one of them is fixed by starting anything",
    ],
  ] as [string, string][],
  // What is true of every row, rather than a row. Rendered in the same list, and deliberately
  // not counted by the heading.
  notes: [
    [
      "Every row carries its remedy",
      ": the command that fixes it, wrapped and marked once, because repeating the arrow per line reads as several actions where there is one",
    ],
    ["Or as JSON", ": --json gives an installer the same report, verdicts and remedies included"],
  ] as [string, string][],
  note: [
    "The same check runs inside the installer, and the same report is what ",
    { b: "a clean Windows 11 virtual machine" },
    " is driven through on the way to a release, because a red row nobody has ever executed is not a check.",
  ] as Rich,
};

/* ------------------------------------------------------------------ engine */

export const engine = {
  eyebrow: "Provisioning",
  // DD159. Both counts are ProvisioningStep's own, and installer.iss draws a progress bar
  // against the same enum — so three files agree about the number instead of two.
  heading: `${Spelled(stepCount())} steps, and it stops at the one that broke`,
  intro: [
    "Provisioning runs from an installer, where there is no terminal to answer a prompt in. So every step is unattended, every step is named, and the run stops at the first failure. A report listing six failures where there was one is a report nobody can act on.",
  ] as Rich,
  steps: [
    {
      n: "1",
      title: "Acquire, and verify",
      body: [
        `${Spelled(acquireCount())} of the ${spelled(stepCount())} steps, one per artefact: the Alpine root filesystem, the static Linux engine, the Windows CLI zip, and the Compose and Buildx plugins, each downloaded and checked against a `,
        { b: "digest recorded in this repository" },
        ", not one served by the same host as the file. Already verified on disk means no second download.",
      ] as Rich,
    },
    {
      n: "2",
      title: "Inspect, before touching WSL",
      body: [
        "The engine tarball's member list is read locally: do the entries share one top directory, and are ",
        { code: "dockerd" },
        ", ",
        { code: "containerd" },
        ", ",
        { code: "runc" },
        " and the rest actually in there? A bad archive is caught before a distribution exists.",
      ] as Rich,
    },
    {
      n: "3",
      title: "Import an owned distro",
      body: [
        { code: "wsl --import freewilly … --version 2" },
        ". A fixed name that is this tool's, so an ",
        { code: "apt upgrade" },
        " or a ",
        { code: "wsl --unregister" },
        " you ran for your own reasons cannot take the engine with it.",
      ] as Rich,
    },
    {
      n: "4",
      title: "Install the engine inside it",
      body: [
        "One non-interactive ",
        { code: "sh" },
        " script under ",
        { code: "set -e" },
        ", so it stops where it broke: ",
        { code: "iptables" },
        " and ",
        { code: "socat" },
        " from apk, the binaries unpacked into ",
        { code: "/usr/local/bin" },
        ", and ",
        { code: "systemd=false" },
        ", because nothing here is a service.",
      ] as Rich,
    },
    {
      n: "5",
      title: "Place the Windows CLI",
      body: [
        { code: "docker.exe" },
        " is extracted to ",
        { code: "…\\FreeWilly\\bin" },
        ". Putting that directory on your PATH is the installer's job, and this step's only job is being the path it points at.",
      ] as Rich,
    },
    {
      n: "6",
      title: "Place Compose and Buildx",
      body: [
        "Both plugins go where the CLI looks for one, so ",
        { code: "docker compose up" },
        " and ",
        { code: "docker buildx build" },
        " work from the install rather than from a second download, pinned by digest like everything else here. Every build the daemon kept is a page in the window, which is also where the ",
        { code: "docker-desktop://" },
        " link buildx prints at the end of a build lands.",
      ] as Rich,
    },
    {
      n: "?",
      ask: true,
      title: "Or just look first",
      body: [
        { code: "--plan" },
        " prints every pinned version, digest and path and reaches nothing at all. ",
        { code: "--acquire" },
        " downloads and verifies, and stops before WSL2 is touched. Both change nothing outside this tool's own directory.",
      ] as Rich,
    },
  ],
  // Titled as the excerpt it is (DD157), and since DD159 it is one: the lines are sliced out
  // of CommandLine.HelpText rather than retyped, so a verb renamed or a description reworded
  // travels here by itself. What was typed here had already drifted from the real output in
  // two places — it named a pipe the help text does not print, and it opened with a summary
  // line the command has never said.
  helpTitle: "freewilly --help: the engine verbs",
  help: helpExcerpt("--plan", "--autostart"),
  helpNote:
    "Exit code 0 means the mode finished; 1 names the step it stopped at. For --status, 1 means the engine is not answering.",
};

/* ------------------------------------------------------------------ pipe */

export const pipe = {
  eyebrow: "The transport",
  headingRuns: [
    "Your ",
    { code: "docker" },
    " commands do not need to know",
  ] as Rich,
  intro: [
    "A Linux ",
    { code: "dockerd" },
    " cannot create a Windows named pipe, because that is a Win32 object. So something on the Windows side has to, or every shell and every script you already have needs a ",
    { code: "DOCKER_HOST" },
    ". FreeWilly is that something.",
  ] as Rich,
  cards: [
    {
      icon: "🔒",
      title: "The ACL is the reason",
      body: [
        "The pipe is created for ",
        { b: "your account and nobody else" },
        ". A forwarded port cannot say that, and full access to the Engine API is full access to the machine, so the hop runs over ",
        { code: "wsl.exe" },
        "'s stdio instead.",
      ] as Rich,
    },
    {
      icon: "🧩",
      title: "Your existing tools, unchanged",
      body: [
        "It is the same pipe name, so the CLI, Compose, Testcontainers, an IDE plugin and whatever your CI script does locally all find it without a setting. Nothing has to be told about FreeWilly.",
      ] as Rich,
    },
    {
      icon: "🧵",
      title: "An API client with no dependencies",
      body: [
        "The app talks HTTP over the pipe directly, a named-pipe stream handed to .NET's own HTTP handler, pinned to Engine API ",
        { code: "v1.43" },
        ". No NuGet package, and no shelling out to ",
        { code: "docker.exe" },
        " once per refresh.",
      ] as Rich,
    },
  ],
};

/* ------------------------------------------------------------------ tray */

export const tray = {
  eyebrow: "In the tray",
  heading: "“Is Docker up?” should be a glance",
  intro: [
    "The icon carries the engine state as a ",
    { b: "shape" },
    ", and colour only reinforces it. At sixteen pixels a hue is a hint: two of these are seen at a glance, by people who may not separate red from green, against a taskbar that is light on one machine and dark on the next. These three are still distinguishable in a screenshot printed in black and white.",
  ] as Rich,
  states: [
    {
      kind: "run" as const,
      title: "Running",
      body: [
        "A filled disc. And it means the engine ",
        { b: "answered" },
        ": the state comes from the event stream's own connection, not from remembering that a start was clicked.",
      ] as Rich,
    },
    {
      kind: "start" as const,
      title: "Starting",
      body: [
        "A ring with a bite out of it: the same outline as stopped and unmistakably not it, which is what tells “on its way up” from “not running” without relying on hue.",
      ] as Rich,
    },
    {
      kind: "stop" as const,
      title: "Stopped",
      body: [
        "A plain ring. ",
        { code: "Start engine" },
        " is one click away in the menu, and it launches the engine in a process the tray does not own.",
      ] as Rich,
    },
  ],
  splitEyebrow: "Deliberately short",
  // DD160. The count is TrayMenu's, and one bullet per item is what makes it assertable — the
  // heading and the list have to move together, and this said four while describing three and
  // promising a Quit the product reversed in DD128.
  splitHeading: `${Spelled(menuCount())} items, because a tray menu that grows is a second app`,
  splitList: [
    [
      { b: "Open window" },
      ": first, and alone above its rule, because it is what a left click on the icon already does. A menu whose opening line is not the thing the click does is a menu hiding it",
    ] as Rich,
    [
      { b: "Start engine" },
      ": enabled only when there is not one running, and it launches the engine in a process the tray does not own. On a machine with no distribution imported it says so instead of pretending to start",
    ] as Rich,
    [
      { b: "Stop engine" },
      ": it stops serving the pipe, kills the daemon and terminates the distribution. WSL powers the virtual machine down itself once nothing else is using it",
    ] as Rich,
    [
      { b: "Start engine with FreeWilly" },
      ": what happens when you are not asking. Opening this tool usually means wanting an engine, so it ships on; turning it off restores the old behaviour exactly, or the setting is decoration",
    ] as Rich,
    [
      { b: "Quit" },
      ": the engine goes with it. It used to stay up, and the asymmetry was stated as the point; measured against the complaint this project exists over it cost more than it saved, because a running engine holds a WSL2 virtual machine and the only way to get those gigabytes back was remembering a second item first. Somebody who quits has said they are done",
    ] as Rich,
  ],
  // The item that is not on that list, and the reason the heading counts five rather than six.
  splitHidden: [
    { b: "One more appears, and only then" },
    ": the item that installs an update exists so the menu's shape is fixed, and it is invisible until a release newer than this build has been found. A verb that is usually a no-op is worse than one that is usually absent",
  ] as Rich,
};

/* ------------------------------------------------------------------ window */

export const windowSection = {
  eyebrow: "The window",
  heading: "One window, and it is the list of containers",
  intro: [
    "Because that is what the tool is opened for. Name, image, state, how long it has been up, and the ports. And the ports are links, because a published ",
    { code: "8080" },
    " is the thing you actually wanted and retyping ",
    { code: "localhost:8080" },
    " is a small daily tax a GUI exists to remove.",
  ] as Rich,
  // DD158. It read "Dark, because Windows is." — contradicted by its own next clause, and by
  // the code: ThemeMode="System" on both windows means the window is whatever Windows is.
  // The claim that survives is the better one to make anyway.
  caption: [
    { b: "Whatever your Windows is." },
    " The window is WPF on the built-in Fluent theme with ",
    { code: "ThemeMode=\"System\"" },
    ", so light and dark follow the OS, with no extra package.",
  ] as Rich,
  detailsEyebrow: "The details",
  detailsHeading: "Correct without being asked, and empty on purpose",
  detailsList: [
    [
      { b: "No refresh button." },
      " The list is a view of the engine: it reads ",
      { code: "/events" },
      " as the daemon writes it, and only the events that change a container list cost a read, because the rest would be a poll in disguise",
    ] as Rich,
    [
      { b: "Started in a terminal, shown here." },
      " A ",
      { code: "docker run" },
      " in any shell appears without you touching the window",
    ] as Rich,
    [
      { b: "The stream re-opens itself" },
      " after every break, so stopping and starting the engine does not leave a window quietly lying",
    ] as Rich,
    [
      { b: "Empty is a designed state" },
      ", and the two reasons a list is empty read differently: only one of them is something you can act on, and that one offers the button",
    ] as Rich,
    [
      { b: "Duplicates folded." },
      " A port published on both address families comes back twice from the API and would otherwise be two identical cells",
    ] as Rich,
    [
      { b: "UDP is text." },
      " Only TCP gets a link, because ",
      { code: "http://localhost:x" },
      " is not where a published UDP port is",
    ] as Rich,
    // DD157. The window has six destinations and this page described three of them, so the
    // one page nobody could have guessed at was the one missing: a build is the only thing
    // here whose record outlives the thing it produced.
    //
    // DD165 added the sixth and the count moved with it, which is the whole of what DD160 was
    // about: a number on this page that nobody updates is a number that goes quietly wrong.
    [
      { b: `${Spelled(destinationCount())} destinations, and Builds is one.` },
      " Containers, images, volumes and ",
      { b: "what has been built here" },
      ", which is also where the ",
      { code: "docker-desktop://" },
      " link buildx prints at the end of every build lands, because the record it names is one the daemon kept and only the address was dead",
    ] as Rich,
    // DD165. No ordinal in this one, deliberately: "the fifth" is a count typed into prose, and
    // this whole file exists because those go stale in silence. What the page is does not need a
    // number — it is the destination the others send you to when they are all empty.
    // DD165, and widened by DD206 once the page stopped being only the journal. Still no ordinal
    // and still no count of what it shows: both would be numbers typed into prose, which is what
    // DD159 removed from this file. What the page is does not need either.
    [
      { b: "Engine is where an empty window explains itself." },
      " The host's own journal, followed while you watch it: every time the engine was brought back, what ",
      { code: "wsl.exe" },
      " said the last time it would not start, and the suspend it did not survive. Beside it, what the machine under the engine is doing: the WSL and kernel versions, the root device and whether it is still writable, what ext4 has recorded against it, and what the virtual disk says it is against what Windows is really charging for it, beside the space used inside and left on the drive: a disk that was handed back keeps its size on paper long after the volume stopped paying for it. And a check that reads the filesystem, which offers to repair it only once it has found something and shown you what, beside a Compact button that drops the build cache, trims the filesystem and hands the freed blocks back. Current Windows builds have sparse virtual disks turned off, and where the hand-back is refused for that reason the page offers the one route left: it asks for administrator rights, shuts every WSL distribution down rather than only this product's, and costs nothing at all if the prompt is declined",
    ] as Rich,
  ],
};

/* ------------------------------------------------------------------ not resident */

export const notResident = {
  icon: "💤",
  heading: "It is not running right now",
  body: [
    [
      "There is no service and no scheduled task. The engine runs for exactly as long as something is holding it. Quit that, and Windows is back to where it was. Autostart exists, it is a single per-user registry value, it is ",
      { b: "off unless you turn it on" },
      ", and turning it off ",
      { b: "removes" },
      " the value rather than setting it to zero.",
    ] as Rich,
    // DD159. The count and the hosts are engine-manifest.json's — this is the claim DD157
    // found stating three artefacts against five, and it is the one on the page where being
    // wrong is a privacy claim rather than a detail.
    [
      `Nothing is uploaded, there is no account, and there is nothing to log into. This tool downloads the ${spelled(artefactCount())} pinned artefacts, from `,
      ...hostRuns(product.artefacts.hosts),
      ", during a provision you asked for.",
    ] as Rich,
    // DD171 reversed DD154's off-by-default check, so this paragraph stopped being about a switch
    // and became a plain statement of the second host. A promise quietly outgrown is worse than one
    // openly revised, which is why it says four a day rather than saying less.
    [
      "It also asks ",
      { code: "api.github.com" },
      " four times a day what the latest release tag is, so it can tell you when a version exists. That request ",
      { b: "carries this product's name and version and nothing about you" },
      " (there is no id, no token and no account to attach one to), and an installer it offers is checked against the SHA-256 published beside it before anything runs. It is not a switch, because a check nobody turns on is a check nobody has.",
    ] as Rich,
  ],
};

/* ------------------------------------------------------------------ non-goals */

// Hoisted so the sentence above can count them (DD159). It is the same defect class as the
// provisioning steps, one source away: the number was typed beside the list it describes, and
// nothing would have noticed a sixth non-goal being added under a paragraph saying five.
const nonGoalItems = [
  {
      title: "Feature parity with Docker Desktop",
      body: "Kubernetes, extensions, a dashboard for everything. The list a user actually opens the app for is short, and stopping there is what keeps this small enough to trust.",
    },
    {
      title: "A fork of the engine",
      body: "Upstream Moby, unpatched, pinned by digest. If the engine misbehaves, that is between you and upstream, and this project has not touched it.",
    },
    {
      title: "macOS and Linux",
      body: "Linux already has the engine, and the entire mechanism here is WSL2 and a Windows named pipe. Portability would mean a different tool wearing the same name.",
    },
    {
      title: "Telemetry, accounts or a sign-in",
      body: "Nothing is measured, nothing is sent, and there is nothing to register. There is no build of this with an opt-out.",
    },
  {
    title: "A resident background service",
    body: "The complaint that sends people looking for an alternative is an engine holding gigabytes from every boot. Not being that is the product, not a preference.",
  },
];

export const nonGoals = {
  eyebrow: "Scope",
  heading: "What it is not",
  intro: [
    `${Spelled(nonGoalItems.length)} things this project has decided against, written down where they can be pointed at. A tool with no stated non-goals is a tool that will eventually be asked for all of them.`,
  ] as Rich,
  items: nonGoalItems,
};

/* ------------------------------------------------------------------ /claude-code page */
// §DD46 — the page for the agent's operator. The read/do split is the highest-leverage
// decision in DD23 and it is invisible to the person who would benefit from it. The whole
// surface is the registry the build reads, so the page cannot list a verb that does not
// exist and a reader is never told a shipped thing that is not.

export const claudeCode = {
  meta: {
    title: "FreeWilly for Claude Code: the friction it removes",
    description:
      "What plain docker costs an agent: a table it cannot read, an inspect paid for four fields, a restart loop billed once per restart, and a permission prompt on every read. And the verb that replaces each one.",
    ogTitle: "FreeWilly for Claude Code",
    ogDescription:
      "The friction plain docker puts on an agent, item by item, with the measured cost of each and the verb that replaces it.",
  },
  eyebrow: "For the agent's operator",
  heading: "Configure it once, and the reads stop asking",
  intro: [
    "Listing containers and deleting a volume are one string to an allowlist, so a user either grants every ",
    { code: "docker" },
    " call (which permits deleting a volume) or approves each one. Splitting the verbs in argv makes it one line of settings, and what that buys is not keystrokes: it is the removal of an interruption from the 90% of agent Docker work that mutates nothing.",
  ] as Rich,
  // The count is read off the verb registry itself (S1, S2), so what follows is the copy
  // around it and never the figure. The banner used to name the block the rest was designed
  // under, which is an address that resolves only inside this repository (DD90) — a reader
  // meeting "Block G" has no way to look it up and no reason to care.
  statusLead: "verbs, and every one of them exists.",
  status: [
    "The list below is the registry the build reads, so a verb that has not landed is not on it. And the ceiling beside each one is the figure a build fails on rather than a number this page claims.",
  ] as Rich,
  allowlistHeading: "The one line that pays for it",
  allowlistLead: "One entry in .claude/settings.json:",
  allowlistLine: "Bash(freewilly read:*)",
  allowlistNote: [
    { code: "read" },
    " is a promise, not a naming convention: a verb under it that writes is a defect. So this single grant removes every prompt on the inspection path, while every ",
    { code: "do" },
    " still asks.",
  ] as Rich,
  // `k` is the verb as it is typed, which is also the key its ceiling lives under in
  // agent-budget.json and the string the registry dispatches on — so one field joins a row
  // to both generated sources, and a row for a verb nobody named cannot claim to exist.
  //
  // Every row here is a verb the registry dispatches (DD90). It used to list five more —
  // read disk, read path, do start, do rm, do prune — under a "designed" mark, and a page
  // that lists what does not exist is asserting it does: the reader who runs `do prune`
  // finds out this page was a plan. The mark went with them rather than the list being
  // unmarked, which would have made the same claim silently. `surface.test.mjs` still
  // fails on a row whose `k` the registry does not carry, so this list cannot drift back.
  readHeading: "read: the inspection path (no prompt)",
  read: [
    { k: "read context", v: "context", d: "the whole machine in one budgeted, terse payload: engine, services, ports, disk, cursor" },
    { k: "read doctor", v: "doctor <name>", d: "the diagnostic join: the verdict and the remedy for one container" },
    { k: "read logs", v: "logs <name>", d: "deduped and budgeted, written to a file the agent Greps, paying for matches, not the whole log" },
    { k: "read ps", v: "ps", d: "the container list, addressed by name, with a cursor" },
    { k: "read ports", v: "ports", d: "what is published, and whether it actually listens from Windows" },
    { k: "read changes", v: "changes --since <cur>", d: "the delta since last session, so N+1 is cheaper than N" },
    { k: "read verify", v: "verify <target>", d: "cheap textual proof a service answers, because the agent cannot see" },
    { k: "read health", v: "health", d: "the machine under the engine: WSL, the distribution, the disk and whether the pipe answers" },
  ],
  doHeading: "do: the mutating path (still asks)",
  do: [
    { k: "do compose up", v: "compose up", d: "brings the project here up, stamped so do reclaim can take it back" },
    { k: "do engine", v: "engine", d: "start or stop the engine itself" },
    { k: "do reclaim", v: "reclaim", d: "an undo scoped to this session's own labels" },
  ],
  pluginHeading: "The plugin that makes it discoverable",
  pluginBody: [
    "A surface nobody discovers is one nobody uses, and the moment it is discoverable is the moment the installer runs. So the Claude Code plugin (the skill, the allowlist entry, and a project brief generated from the live machine) ships with the install rather than being something to go and find.",
  ] as Rich,
  refusesHeading: "What it deliberately refuses",
  refusesLead: [
    "FreeWilly is the substrate; the intelligence is the caller's. It is a CLI over the Engine API and facts Windows already knows, not a second Docker CLI (P10).",
  ] as Rich,
  refuses: [
    { t: "No model", b: "It calls no LLM." },
    { t: "No prompts", b: "It stores none." },
    { t: "No API keys", b: "There is no secret to hold." },
    { t: "No build", b: "That stays docker's." },
    { t: "No push", b: "And so does that." },
    { t: "No registry auth", b: "do compose shells out to yours." },
  ],
};

/* ------------------------------------------------------------------ /claude-code: the friction */
// The problem half of §DD46's page. The read/do split and the allowlist line answer *one* of
// the frictions plain docker puts on an agent — the interruption — and the page shipped
// stating only that one, so a reader who had never felt the others was told the surface
// existed for keystrokes.
//
// Every row is DD23 §3's accounting, and every figure in it is generated: `shape` keys into
// the measured baseline in agent-budget.json and `verb` into that file's ceilings and the
// registry's shipped list (S1, S2, and DD40 §4 — the law that said an unmeasured cost is not
// a figure on the page, now that DD23 has measured it). A row naming a shape the benchmark
// does not measure, or a verb the registry does not have, fails the build rather than
// rendering a blank where its argument was.
//
// `today` is what the command actually does, not a caricature: docker is a good CLI for a
// person at a keyboard, and every one of these is a consequence of being built for one.

export const friction = {
  eyebrow: "The friction this removes",
  heading: "What plain docker costs an agent, and what replaces it",
  intro: [
    "None of this is a defect in Docker. Every one of these is what a CLI built for a person at a keyboard does to a reader that pays per token, cannot see the screen, and has to ask permission. And the costs on the left are ",
    { b: "measured" },
    ", not estimated.",
  ] as Rich,
  baselineLead: "The canonical task, measured:",
  baselineNote: [
    "Measured over the Engine API's compact payload, which is the transport this project's own surface is built on, so read it as a ",
    { b: "floor" },
    " for today's cost, not a ceiling. ",
    { code: "docker inspect" },
    " prints indented JSON, so an agent going through the CLI pays several times this for the same entity. The ceilings on the right are asserted by a test: a response that grew fails the build.",
  ] as Rich,
  todayLabel: "docker today",
  hereLabel: "here",
  items: [
    {
      t: "The output is a table for a person, and the reader is not one",
      today: {
        cmd: "docker ps -a",
        shape: "containers.list",
        body: [
          "Columns truncate, rows come back in creation order (which moves the moment anything is recreated, so two reads cannot be diffed), and nothing carries a cursor. So a session re-reads the whole machine three to five times as state moves, and that re-discovery, not the log, is the largest single driver of the total.",
        ] as Rich,
      },
      here: {
        cmd: "freewilly read context",
        verb: "read context",
        body: [
          "One payload: the engine, every container with its state and compose address, the published ports, the disk, and a cursor that fingerprints the machine. Sorted by name, so it caches and a diff means something. It also answers the canonical question, ",
          { code: "OOM  limit=512m" },
          ", in the first call, with no second one.",
        ] as Rich,
      },
    },
    {
      t: "Four fields cost the whole entity",
      today: {
        cmd: "docker inspect api",
        shape: "container.inspect",
        body: [
          "Three to six hundred lines of JSON, of which an agent reads four: the exit code, whether it was OOM-killed, the port bindings and the mounts. There is no projection to ask for, so the whole entity tree, every default and every piece of authoring metadata, is paid for to learn four things.",
        ] as Rich,
      },
      here: {
        cmd: "freewilly read doctor api",
        verb: "read doctor",
        body: [
          "The same join, returned as a verdict and a remedy per row, over the model the preflight already uses: state, memory, restarts, health, ports and mounts. What comes back is the conclusion and the action, not the fields they were read from.",
        ] as Rich,
      },
    },
    {
      t: "A restart loop is billed once per restart",
      today: {
        cmd: "docker logs --tail 200 api",
        shape: "container.logs.tail200",
        body: [
          { code: "--tail" },
          " is the only instrument: no dedup, no level filter, no cursor, no ceiling. A container that restarted eight times hands over the same stack trace eight times, and the caller either truncates blind or pays for all of it.",
        ] as Rich,
      },
      here: {
        cmd: "freewilly read logs api --dedup --level warn --out .freewilly/logs/api.log",
        verb: "read logs",
        body: [
          { code: "--dedup" },
          " collapses an identical line to one and a count, across the whole read rather than only adjacent ones, because the copies of a trace are separated by everything each run printed. ",
          { code: "--level" },
          " keeps every line that did not say what it was, so the filter can narrow the log without hiding the answer. And ",
          { code: "--out" },
          " writes it to a file the agent Greps, paying for the lines that match instead of for the log.",
        ] as Rich,
      },
    },
    {
      t: "A truncated read is indistinguishable from a log that ended",
      today: {
        cmd: "docker logs --tail 200 api | head",
        body: [
          "A cut arrives looking exactly like the end of the output. Nothing in what comes back says a line was dropped or where to resume, so a reader draws a conclusion from a page that was never the whole story, and nobody finds out.",
        ] as Rich,
      },
      here: {
        cmd: "truncated 37 more line(s): budget reached, read on from the cursor",
        body: [
          "Every budgeted payload here cuts from the end, states how much it cut, and prints the cursor to read on from. A silent truncation is the one thing a ceiling must not become, so it is the property the tests hold rather than the size.",
        ] as Rich,
      },
    },
    {
      t: "The id is the currency, and it changes on every recreate",
      today: {
        cmd: "docker inspect 9c1f0b7a4e2d…",
        body: [
          "A 64-hex id has to be threaded across calls by hand, and it is gone the next time the container is recreated. Meanwhile the same service is called one thing by ",
          { code: "docker ps" },
          " and another by ",
          { code: "docker compose ps" },
          ", so an agent holding one name cannot always use it with the other command.",
        ] as Rich,
      },
      here: {
        cmd: "freewilly read doctor svc:shop/api",
        body: [
          "The address is the name: the container's, or ",
          { code: "svc:<project>/<service>" },
          " read off the compose labels the daemon already carries. Ids stay valid and stop being currency, so a name learned in the first call still works in the fifth.",
        ] as Rich,
      },
    },
    {
      t: "The last question is one Docker does not answer at all",
      today: {
        cmd: "docker port api",
        body: [
          "This reports the mapping the daemon intends, which is not the same claim as ",
          { i: "8080 answers from Windows" },
          ": a running container with a bound port can answer nothing. And when the bind fails, ",
          { code: "port is already allocated" },
          " does not say what holds it, because the daemon does not know. So the agent cannot see whether its own work landed, and closing that gap means coming back to you to look, the most expensive unit in the system.",
        ] as Rich,
      },
      here: {
        cmd: "freewilly read verify svc:shop/api",
        verb: "read verify",
        body: [
          "FreeWilly is a Windows process, so it can join what the daemon cannot: whether the host port actually listens, which PID holds it, whether a rival engine is answering the pipe, whether a stale context is pointing the CLI elsewhere. ",
          { code: "read doctor" },
          " already carries the port fact; ",
          { code: "read verify" },
          " is the standalone proof, and ",
          { code: "read ports" },
          " is the refusal that names the process holding the port.",
        ] as Rich,
      },
    },
    {
      t: "Every call is a permission decision",
      today: {
        cmd: "Bash(docker:*)",
        body: [
          { code: "docker ps" },
          " and ",
          { code: "docker rm -f -v" },
          " are the same string to an allowlist. No Docker tool can express the rule a user actually wants, because ",
          { code: "docker" },
          " mixes both in one verb namespace, so you either grant the lot, which permits deleting a volume, or approve every read by hand.",
        ] as Rich,
      },
      here: {
        cmd: "Bash(freewilly read:*)",
        body: [
          "Reads and writes are separate words in argv, so the rule is one line, and it is enforced under it: a read verb is written against a handle with no start, no remove and no prune on it, and a test drives every one of them and requires each request it made to be a GET.",
        ] as Rich,
      },
    },
    {
      t: "Nothing carries over, so session N+1 costs what session N did",
      today: {
        cmd: "docker ps -a   # again, from nothing",
        body: [
          "There is no cursor and no delta, so the next session re-derives the machine the last one already learned. The most expensive read on this page is the one that was paid for yesterday.",
        ] as Rich,
      },
      here: {
        cmd: "freewilly read changes --since c:4f21a0",
        body: [
          "Every pack already prints a state cursor, and the tray already holds the engine's event stream open for the window, so the delta is a cursor over a stream the user started rather than a daemon this adds. It is the only mechanism here that makes a second session cheaper than the first.",
        ] as Rich,
      },
    },
  ],
  footer: [
    "The five-call session on the home page is the same task run through these verbs. Its per-call figures are the ceilings above, which is why they are the numbers a build fails on rather than the numbers a page claims.",
  ] as Rich,
};

/* ------------------------------------------------------------------ /compare page */
// §DD47 — a visitor arrives having already decided against something. The honest question
// is narrow: what does this do that the alternative does not. A matrix of green ticks is
// not believed, so every alternative gets a column for what it is genuinely better at, and the
// rows are grouped by the law each comes from so the matrix argues rather than tallies.

export const compare = {
  meta: {
    title: "FreeWilly vs Docker Desktop, Rancher, Podman, plain WSL2",
    description:
      "What FreeWilly does that Docker Desktop, Rancher Desktop, Podman Desktop or a hand-rolled WSL2 daemon does not, and what each of those is genuinely better at.",
    ogTitle: "FreeWilly: the honest comparison",
    ogDescription:
      "Checkable rows grouped by the law each comes from, and a column for what every alternative wins.",
  },
  eyebrow: "Against the alternatives",
  heading: "What this does that the others do not, and where they win",
  intro: [
    "You arrive having already decided against something. A matrix that wins every row is one nobody believes, so this one is grouped by the law each row comes from, and every alternative keeps the column it genuinely wins.",
  ] as Rich,
  columns: ["FreeWilly", "Docker Desktop", "Rancher Desktop", "Podman Desktop", "WSL2 daemon"],
  legend: [
    { sym: "✓", label: "yes" },
    { sym: "~", label: "partial" },
    { sym: "✗", label: "no" },
  ],
  groups: [
    {
      law: "Cost & access",
      rows: [
        { cap: "Free at any headcount", cells: ["✓", "✗", "✓", "✓", "✓"] },
        { cap: "Per-user install, no admin prompt", cells: ["✓", "✗", "✗", "~", "✗"] },
      ],
    },
    {
      law: "Footprint: nothing resident",
      rows: [
        { cap: "No service or VM held from every boot", cells: ["✓", "✗", "✗", "~", "~"] },
      ],
    },
    {
      law: "Compatibility",
      rows: [
        { cap: "Standard docker_engine pipe, no DOCKER_HOST", cells: ["✓", "✓", "✓", "✗", "✗"] },
        // DD158. The row the matrix was missing, and it is the one a hand-rolled WSL2 daemon
        // loses on work rather than on principle: both plugins are acquired, digest-checked
        // and placed where the CLI looks, by the same provisioner as the engine.
        { cap: "Compose and Buildx installed, pinned by digest", cells: ["✓", "✓", "✓", "~", "✗"] },
        { cap: "Cross-platform (macOS, Linux)", cells: ["✗", "✓", "✓", "✓", "✗"] },
      ],
    },
    {
      law: "For an agent",
      rows: [
        { cap: "Read/do split at the argv level", cells: ["✓", "✗", "✗", "✗", "✗"] },
      ],
    },
    {
      law: "Breadth: where an alternative leads",
      rows: [
        { cap: "Kubernetes built in", cells: ["✗", "✓", "✓", "~", "✗"] },
        { cap: "Extensions / build cloud", cells: ["✗", "✓", "✗", "✗", "✗"] },
        { cap: "Rootless, daemonless", cells: ["✗", "✗", "✗", "✓", "✗"] },
        { cap: "Commercial support", cells: ["✗", "✓", "~", "~", "✗"] },
      ],
    },
  ],
  winsHeading: "What each alternative is genuinely better at",
  wins: [
    { name: "Docker Desktop", body: "Kubernetes, an extensions marketplace, a build cloud, and somebody to call. Pick it when you need those, or want a vendor on the hook." },
    { name: "Rancher Desktop", body: "Cross-platform and ships Kubernetes. Pick it on macOS or Linux, or for a Kubernetes dev loop." },
    { name: "Podman Desktop", body: "Daemonless and rootless. Pick it when a rootless, no-daemon model is the requirement." },
    { name: "Plain WSL2 daemon", body: "Free of this project entirely. Pick it if you would rather wire dockerd into WSL2 and manage the pipe yourself." },
  ],
  winsFooter: [
    "What is left is the axis where this one wins: a per-user install with no admin prompt, nothing resident, the standard pipe so no tool needs telling, and an agent surface that splits reads from writes.",
  ] as Rich,
};

/* ------------------------------------------------------------------ download */
// DD153. Until v1.0.0 there was nothing to link to, and the constitution names a site that
// implied otherwise as the worst defect available to this page — so the fix is a section
// that states what the installer actually is, not a badge.
//
// No version number is typed here, and that is S1/S2 rather than laziness: a version is a
// figure this page cannot generate, and one typed in prose is wrong from the next release
// onwards while still reading as current. `releases/latest` resolves to whatever shipped,
// and the checksum a reader wants is on the page it opens.
export const download = {
  eyebrow: "Take the installer",
  heading: "One file, and it asks for no administrator",
  intro: [
    "The release page carries the installer and the ",
    { code: "SHA256SUMS.txt" },
    " to check it against, and nothing else to choose between. It installs into ",
    { code: "%LOCALAPPDATA%\\FreeWilly" },
    " for your account alone, which is what reaches a managed corporate laptop, the machine this project exists for. The WSL2 feature the engine needs may still want elevation of its own; the installer runs the preflight and says so rather than failing halfway through a download.",
  ] as Rich,
  // Emoji-free for the same reason as the hero's three, and the arrow on the button below is
  // not the same thing: that one is what the control does, these were ornament on a fact.
  facts: [
    "Windows 10 build 19041 or later, 64-bit",
    "No prerequisite: the .exe carries its own .NET runtime",
    "SHA-256 published beside the file",
  ],
  cta: "⬇ Download for Windows",
  ctaShort: "⬇ Download",
  secondary: "Release notes and checksum",
  // The uninstall belongs on the download section because it is the question a reader asks
  // before installing, not after: what happens to the containers.
  note: [
    "Uninstalling removes what was installed and ",
    { b: "asks about what was created" },
    ". The ",
    { code: "freewilly" },
    " distribution holds every image, container and volume you have, so it is never deleted without a question, and an unattended uninstall keeps it.",
  ] as Rich,
};
