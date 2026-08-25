import { download, navLinks, parentUrl, releasesUrl, repoUrl } from "../lib/site-content";
import { ThemeToggle } from "./ui/ThemeToggle";

export function Nav() {
  return (
    <nav>
      <div className="wrap">
        <div className="nav-left">
          <a className="brand" href="/freewilly/">
            <img src="/freewilly/logo.svg" alt="" />
            FreeWilly
          </a>
          <a className="parent" href={parentUrl} title="alegauss: small developer tools">
            <span className="pre">part of</span>
            <b>alegauss</b>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M7 17 17 7" />
              <path d="M9 7h8v8" />
            </svg>
          </a>
        </div>
        <div className="nav-links">
          {navLinks.map((link) => (
            <a key={link.href} href={link.href}>
              {link.label}
            </a>
          ))}
          {/* DD153: the primary button is the download now that there is one. GitHub keeps
              its place as the ghost beside it — the repository is what a reader checks
              before installing anything, and demoting it to a footer link would be the
              wrong half of this trade. */}
          <a className="btn btn-primary" href={releasesUrl}>
            {download.ctaShort}
          </a>
          <a className="btn btn-ghost" href={repoUrl}>
            ★ View on GitHub
          </a>
          <ThemeToggle />
        </div>
      </div>
    </nav>
  );
}
