import type { Rich } from "./site-content";
import { Spelled, acquireCount, artefactCount, rowCount, spelled, stepCount } from "./product";

// §DD48 — the five depth pages, one record each. The route, the title and the description
// are all read off the same record (in routes.tsx), so a new pillar cannot ship
// half-declared or untitled: add a record here and its route, its <head> and its page all
// appear together, or none of them do.
//
// DD159. The counts in these records are the same counts the landing page states, so they come
// from the same generated module: a depth page is where a reader goes to check the summary, and
// the two disagreeing is worse than either being stale alone.

export interface FeatureSection {
  heading: string;
  body?: Rich;
  list?: Rich[];
}

export interface FeatureRecord {
  slug: string;
  title: string;
  description: string;
  ogTitle: string;
  ogDescription: string;
  eyebrow: string;
  heading: string;
  lead: Rich;
  /** a diagram key resolved to markup in the page component */
  figure?: "pipe" | "window" | "preflightTerminal" | "empty";
  sections: FeatureSection[];
}

export const features: FeatureRecord[] = [
  {
    slug: "preflight",
    title: `Preflight: why Docker will not run here, in ${spelled(rowCount())} rows`,
    description:
      `The ${spelled(rowCount())} common causes of a Docker failure on Windows, each with the command that fixes it, read-only and exit 1 while a blocking row is not green.`,
    ogTitle: "FreeWilly: the preflight",
    ogDescription: `${Spelled(rowCount())} checks, ${spelled(rowCount())} remedies, and the hypervisor-before-firmware order.`,
    eyebrow: "Before anything is installed",
    heading: "The preflight",
    lead: [
      `“It does not work on my machine” has ${spelled(rowCount())} common causes on Windows, and ${spelled(rowCount())} different remedies. `,
      { code: "freewilly --preflight" },
      " names the one you have, prints the command that fixes it, changes nothing, and exits ",
      { code: "1" },
      " so an installer can stop rather than fail halfway.",
    ],
    figure: "preflightTerminal",
    sections: [
      {
        heading: `${Spelled(rowCount())} rows, each with its remedy`,
        list: [
          ["Windows build: 19041 or later, because below it no configuration gets a WSL2 kernel."],
          ["Hardware virtualization: read the hypervisor bit first, because Windows reports the firmware bit as off once something has claimed it."],
          ["WSL2: a missing wsl.exe, a kernel-less half-install, and “new distros default to WSL1” are three states with three lines."],
          ["Container engine: anything else owning the docker command or the docker_engine pipe, because two engines on one pipe leaves neither working."],
          ["Docker context: where your own docker command points, because an engine that is running and a client aimed somewhere else fail with the same sentence."],
        ],
      },
      {
        heading: "The order is the argument",
        body: [
          "Reading the hypervisor before the firmware bit is the row that took an argument to get right: the naive order sends a plainly virtualizing machine into a BIOS to enable what is already on. ",
          { code: "--json" },
          " gives an installer the same report, verdicts and remedies included, and the same check runs inside the installer and against a clean Windows 11 VM on the way to a release.",
        ],
      },
    ],
  },
  {
    slug: "engine",
    title: "The engine: upstream Moby into a distro it owns",
    description:
      `${Spelled(stepCount())} unattended steps that stop at the first failure: acquire and verify ${spelled(artefactCount())} artefacts by digest, inspect the archive, import an owned WSL2 distro, install inside it, and place the CLI with its Compose and Buildx plugins.`,
    ogTitle: "FreeWilly: the engine",
    ogDescription: `${Spelled(stepCount())} steps, pinned by digest, stopping at the one that broke.`,
    eyebrow: "Provisioning",
    heading: "The engine",
    lead: [
      "Provisioning runs from an installer, where there is no terminal to answer a prompt. So every step is unattended, every step is named, and the run stops at the first failure. A report of six failures where there was one is a report nobody can act on.",
    ],
    sections: [
      {
        heading: `${Spelled(stepCount())} steps, pinned by digest`,
        list: [
          [`Acquire and verify. ${Spelled(artefactCount())} artefacts in ${spelled(acquireCount())} steps: the Alpine rootfs, the static Linux engine, the Windows CLI zip, and the Compose and Buildx plugins. Each checked against a digest recorded in this repository, not one served by the same host as the file.`],
          ["Inspect before touching WSL. The tarball's member list is read locally, so a bad archive is caught before a distribution exists."],
          [{ code: "wsl --import freewilly … --version 2" }, ". The name is fixed and it is this tool's, so your own apt upgrade or unregister cannot take the engine with it."],
          ["Install inside it with one non-interactive sh script under set -e: iptables and socat from apk, the binaries into /usr/local/bin, systemd=false."],
          ["Place the Windows CLI, docker.exe, under %LOCALAPPDATA%\\FreeWilly\\bin, for the installer to add to PATH."],
          ["Place the plugins. Compose and Buildx go where the CLI looks for one, so docker compose and docker buildx work from the install rather than from a second download."],
        ],
      },
      {
        heading: "Look before you download",
        body: [
          { code: "--plan" },
          " prints every pinned version, digest and path and reaches nothing at all. ",
          { code: "--acquire" },
          " downloads and verifies and stops before WSL2 is touched. Both change nothing outside this tool's own directory.",
        ],
      },
    ],
  },
  {
    slug: "pipe",
    title: "The pipe: why a named pipe, not a port",
    description:
      "A Linux dockerd cannot create a Windows named pipe, so FreeWilly serves docker_engine and forwards over wsl.exe stdio. The ACL grants your account and nobody else, which a forwarded port cannot express.",
    ogTitle: "FreeWilly: the pipe",
    ogDescription: "Why a named pipe and not a port, and the ACL that is the reason.",
    eyebrow: "The transport",
    heading: "The pipe",
    lead: [
      "A Linux ",
      { code: "dockerd" },
      " cannot create a Windows named pipe, because that is a Win32 object. So something on the Windows side has to, or every shell and script you already have needs a ",
      { code: "DOCKER_HOST" },
      ". FreeWilly is that something.",
    ],
    figure: "pipe",
    sections: [
      {
        heading: "The ACL is the reason it is not a port",
        body: [
          "The pipe is created for your account and nobody else. Full access to the Engine API is full access to the machine, and a TCP port every local process can reach cannot express “only me”, so the hop runs over ",
          { code: "wsl.exe" },
          "'s stdio to ",
          { code: "socat" },
          " and on to the unix socket, with no forwarded port anywhere on the path.",
        ],
      },
      {
        heading: "Your existing tools, unchanged",
        body: [
          "It is the standard pipe name, so the CLI, Compose, Testcontainers and IDE plugins find the engine with no setting. The app itself talks HTTP over the pipe directly, a named-pipe stream handed to .NET's own handler, pinned to Engine API ",
          { code: "v1.43" },
          ", with no NuGet dependency.",
        ],
      },
    ],
  },
  {
    slug: "window",
    title: "The window: the list, actions, logs, a shell, images and volumes",
    description:
      "One window that is the list of containers, with ports as links; acting on a container; logs and a shell for what the state does not say; and images and volumes made legible.",
    ogTitle: "FreeWilly: the window",
    ogDescription: "The tray, the container list, the logs, the shell, images and volumes.",
    eyebrow: "The window",
    heading: "The window",
    lead: [
      "One window, and it is the list of containers: name, image, state, uptime and the ports, because a published ",
      { code: "8080" },
      " is the thing you actually wanted. It reads ",
      { code: "/events" },
      " as the daemon writes it, so nothing here polls and nothing needs a refresh button.",
    ],
    figure: "window",
    sections: [
      {
        heading: "What it is opened for",
        list: [
          ["The list: name, image, state, uptime and ports, with a published TCP port a link that opens in your browser and an exposed or UDP port left as plain text."],
          ["Acting on a container: start, stop, restart and remove, where the work is the pending state and the confirmation, not the four endpoints."],
          ["Logs, because a container that exits immediately shows a state and nothing about the cause."],
          ["A shell inside a container, because anything the log does not say is otherwise unreachable."],
          ["Images and volumes: which layers are dangling and which are in use, and making an irreversible volume deletion legible rather than reclaiming space blindly."],
        ],
      },
      {
        heading: "Whatever your Windows is",
        body: [
          "WPF on the built-in Fluent theme with ",
          { code: "ThemeMode=\"System\"" },
          ", so light and dark follow the OS with no extra package. Empty is a designed state: “the engine is not running” offers a Start button, “no containers” does not.",
        ],
      },
    ],
  },
  {
    slug: "agent-surface",
    title: "The agent surface: the context pack, read doctor, teaching errors",
    description:
      "One budgeted context pack answers a session's opening questions; read doctor is the diagnostic join; and every refusal carries the Windows fact that explains it.",
    ogTitle: "FreeWilly: the agent surface",
    ogDescription: "The context pack, read doctor, and teaching errors with the Windows join.",
    eyebrow: "For an agent",
    heading: "The agent surface",
    lead: [
      "The primary operator is a coding agent, and the surface is shaped for it: one call replaces a session, a file beats a stream, names are the address, and errors are instructions. Every verb below is one the CLI dispatches today.",
    ],
    sections: [
      {
        heading: "The context pack: one call replaces a session",
        body: [
          "One deterministic, budgeted payload answers everything an agent asks at the start of a session (engine, services, ports, disk and a cursor) in a terse line format, not JSON, because entity JSON spends most of its bytes on punctuation and repeated keys. Roughly 130 tokens against the five commands and ~20k it replaces.",
        ],
      },
      {
        heading: "read doctor, and teaching errors",
        body: [
          { code: "read doctor <name>" },
          " is the diagnostic join over the preflight's verdict model, pointed at a container: the verdict and the remedy in one call. And every refusal carries what was wrong, what is allowed, the nearest match, a correct example, and the Windows fact that explains it, so an error does not cost a round trip to interpret.",
        ],
      },
      {
        heading: "Configure it",
        body: [
          "The read/do split is one allowlist line, and it is the highest-leverage decision on this surface. The page for the agent's operator has the entry, the calls a session opens with, and what the surface refuses.",
        ],
      },
    ],
  },
];
