import React, {useEffect} from 'react';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import clsx from 'clsx';

/* ── Scroll-reveal hook ──────────────────────────────────────────────── */
function useScrollReveal(className = 'reveal') {
  useEffect(() => {
    const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (prefersReducedMotion) return;

    const els = document.querySelectorAll<HTMLElement>(`.${className}`);
    if (!els.length) return;

    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add('visible');
            observer.unobserve(entry.target);
          }
        });
      },
      {threshold: 0.12, rootMargin: '0px 0px -40px 0px'},
    );

    els.forEach((el) => observer.observe(el));
    return () => observer.disconnect();
  }, [className]);
}

/* ── App screenshot with window chrome ───────────────────────────────── */
function AppScreenshot({src, alt, caption}: {src: string; alt: string; caption?: string}) {
  return (
    <div className="screenshot-frame">
      <div className="window-chrome">
        <span className="window-dot window-dot-red" />
        <span className="window-dot window-dot-yellow" />
        <span className="window-dot window-dot-green" />
        {caption && <span className="window-title">{caption}</span>}
      </div>
      <img src={src} alt={alt} loading="lazy" />
    </div>
  );
}

/* ── Feature card ────────────────────────────────────────────────────── */
interface Feature {
  icon: string;
  title: string;
  desc: string;
}

const FEATURES: Feature[] = [
  {
    icon: '⚡',
    title: 'Instant capture',
    desc: 'Open from the system tray or a global hotkey. Notey is out of the way until you need it — then it\'s there instantly.',
  },
  {
    icon: '🗂️',
    title: 'Slash commands',
    desc: 'Use /topic, /meeting, /task, and dynamic folder commands to route notes to exactly the right place without touching the file system.',
  },
  {
    icon: '🤖',
    title: 'AI formatting',
    desc: 'OCR and AI process your drafts into well-structured Obsidian markdown — canonical people references, tags, and headings included.',
  },
  {
    icon: '🔗',
    title: 'Obsidian-native',
    desc: 'Every note lands in your existing vault with proper [[wikilinks]], inline tags, and same-day headings for topic pages.',
  },
  {
    icon: '📸',
    title: 'Screen snips',
    desc: 'Capture screenshots directly into the editor. Images are stored in your vault; OCR extracts text automatically for AI processing.',
  },
  {
    icon: '📋',
    title: 'Smart paste',
    desc: 'Paste tables from Word, Excel, or Google Docs and get clean GitHub-flavored markdown pipe tables automatically.',
  },
  {
    icon: '✅',
    title: 'Task tracking',
    desc: 'Tasks from any note are routed to Notes/tasks.md with proper markdown checkbox formatting, ready for Obsidian task views.',
  },
  {
    icon: '🌙',
    title: 'Dark-first editor',
    desc: 'A focused, minimal markdown editor with syntax highlighting, table navigation, and a fixed-width font for aligned writing.',
  },
];

/* ── Doc section cards ────────────────────────────────────────────────── */
interface DocCard {
  icon: string;
  title: string;
  desc: string;
  to: string;
}

const DOC_CARDS: DocCard[] = [
  {
    icon: '🚀',
    title: 'Getting started',
    desc: 'Install, configure, and make your first note.',
    to: '/docs/getting-started/installation',
  },
  {
    icon: '🔧',
    title: 'Features',
    desc: 'Deep dives into the editor, slash commands, and AI.',
    to: '/docs/features/obsidian-vault',
  },
  {
    icon: '⚙️',
    title: 'Operations',
    desc: 'Diagnostics, logging, and troubleshooting.',
    to: '/docs/operations/diagnostics',
  },
  {
    icon: '📦',
    title: 'Deployment',
    desc: 'Windows packaging and update distribution.',
    to: '/docs/deployment/windows-publishing',
  },
];

/* ── Gallery item ────────────────────────────────────────────────────── */
interface GalleryItem {
  src: string;
  alt: string;
  caption: string;
  label: string;
}

const GALLERY: GalleryItem[] = [
  {
    src: '/img/table.png',
    alt: 'Notey markdown table editor',
    caption: 'notey — markdown tables',
    label: 'Smart markdown tables with aligned pipes and Tab navigation between cells.',
  },
  {
    src: '/img/image-preview.png',
    alt: 'Notey image preview dialog',
    caption: 'notey — image preview',
    label: 'Preview any embedded image inline with file metadata.',
  },
  {
    src: '/img/recent-note.png',
    alt: 'Notey open recent note dialog',
    caption: 'notey — recent notes',
    label: 'Jump back to any recent vault note with a searchable quick-open dialog.',
  },
];

/* ── Main page component ──────────────────────────────────────────────── */
export default function Home(): React.ReactElement {
  const {siteConfig} = useDocusaurusContext();

  useScrollReveal('reveal');
  useScrollReveal('reveal-left');
  useScrollReveal('reveal-right');

  return (
    <Layout title={siteConfig.title} description={siteConfig.tagline}>
      {/* ── Hero ── */}
      <header className="hero-section">
        <div className="hero-badge">✦ Obsidian-compatible note capture</div>

        {/* Logo + wordmark lockup */}
        <div className="hero-logo-lockup">
          <img
            src="/img/logo.png"
            alt="Notey logo"
            className="hero-logo"
            width={88}
            height={88}
          />
          <h1 className="hero-title">Notey</h1>
        </div>

        <p className="hero-subtitle">{siteConfig.tagline}</p>
        <div className="hero-cta-row">
          <Link className="btn-primary" to="/docs/getting-started/installation">
            Get started →
          </Link>
          <Link className="btn-secondary" to="https://github.com/mzbrau/notey/releases">
            Download release
          </Link>
        </div>

        {/* Hero screenshot */}
        <div className="hero-screenshot reveal">
          <AppScreenshot
            src="/img/notes.png"
            alt="Notey main editor showing a topic note with slash commands"
            caption="notey — editor"
          />
        </div>
      </header>

      <main>
        {/* ── Features ── */}
        <section className="features-section">
          <p className="section-label reveal">What Notey does</p>
          <h2 className="section-title reveal">Built for the way you think</h2>
          <p className="section-subtitle reveal">
            Capture a thought, route it to the right place, and let AI shape it into a
            structured Obsidian note — without interrupting your flow.
          </p>
          <div className="features-grid">
            {FEATURES.map((f) => (
              <div key={f.title} className={clsx('feature-card', 'reveal')}>
                <span className="feature-icon" aria-hidden="true">{f.icon}</span>
                <div className="feature-title">{f.title}</div>
                <p className="feature-desc">{f.desc}</p>
              </div>
            ))}
          </div>
        </section>

        {/* ── Screenshots showcase ── */}
        <section className="screenshots-section">
          <div className="screenshots-inner">
            <p className="section-label reveal">In action</p>
            <h2 className="section-title reveal">A minimal tool with a lot going on</h2>

            {/* Row 1 — Slash commands */}
            <div className="screenshots-feature-row">
              <div className="reveal-left">
                <AppScreenshot
                  src="/img/slash-command.png"
                  alt="Notey slash command autocomplete showing /customer with dropdown options"
                  caption="notey — slash commands"
                />
              </div>
              <div className="feature-text reveal-right">
                <h3>Route every note with a slash</h3>
                <p>
                  Type <code>/customer</code>, <code>/topic</code>, <code>/meeting</code>, or any
                  dynamic folder name and Notey autocompletes from your vault. Your note lands in
                  exactly the right place — no file browsing required.
                </p>
                <ul className="feature-tag-list">
                  <li className="feature-tag">/topic</li>
                  <li className="feature-tag">/meeting</li>
                  <li className="feature-tag">/customer</li>
                  <li className="feature-tag">/project</li>
                  <li className="feature-tag">Dynamic folders</li>
                </ul>
              </div>
            </div>

            {/* Row 2 — Meeting capture */}
            <div className="screenshots-feature-row reverse">
              <div className="reveal-right">
                <AppScreenshot
                  src="/img/meeting.png"
                  alt="Notey meeting note showing AI-formatted attendees, action items, and decisions"
                  caption="notey — meeting notes"
                />
              </div>
              <div className="feature-text reveal-left">
                <h3>Capture meetings as structured notes</h3>
                <p>
                  Add <code>/meeting</code> and Notey routes the note as a meeting record — with
                  date, attendees as vault wikilinks, action items, and decisions all structured
                  by AI before saving to your vault.
                </p>
                <ul className="feature-tag-list">
                  <li className="feature-tag">Attendee wikilinks</li>
                  <li className="feature-tag">Action items</li>
                  <li className="feature-tag">AI formatting</li>
                  <li className="feature-tag">Date context</li>
                </ul>
              </div>
            </div>

            {/* Row 3 — AI assistant */}
            <div className="screenshots-feature-row">
              <div className="reveal-left">
                <AppScreenshot
                  src="/img/notey-assistant.png"
                  alt="Notey AI assistant proposing table row additions with structured changes preview"
                  caption="notey — AI assistant"
                />
              </div>
              <div className="feature-text reveal-right">
                <h3>AI that proposes, you decide</h3>
                <p>
                  Ask the Notey assistant to extend a table, rephrase a section, or assign tasks
                  to people. It shows a structured diff of every proposed change — you review and
                  apply with one click, or discard entirely.
                </p>
                <ul className="feature-tag-list">
                  <li className="feature-tag">Structured diffs</li>
                  <li className="feature-tag">Table editing</li>
                  <li className="feature-tag">People assignment</li>
                  <li className="feature-tag">One-click apply</li>
                </ul>
              </div>
            </div>

            {/* Row 4 — Task tracking */}
            <div className="screenshots-feature-row reverse">
              <div className="reveal-right">
                <AppScreenshot
                  src="/img/tasks.png"
                  alt="Notey task panel showing tasks grouped by This Week, Next Week, and Future"
                  caption="notey — tasks"
                />
              </div>
              <div className="feature-text reveal-left">
                <h3>Tasks in one place, across every note</h3>
                <p>
                  A live task panel aggregates every <code>- [ ] task</code> from your vault,
                  grouped by due date. Tasks written in any note automatically appear here — no
                  separate task app needed.
                </p>
                <ul className="feature-tag-list">
                  <li className="feature-tag">Due date grouping</li>
                  <li className="feature-tag">Vault-wide view</li>
                  <li className="feature-tag">Markdown checkboxes</li>
                  <li className="feature-tag">Inline creation</li>
                </ul>
              </div>
            </div>
          </div>
        </section>

        {/* ── Gallery — three more feature shots ── */}
        <section className="gallery-section">
          <p className="section-label reveal">More features</p>
          <h2 className="section-title reveal">Every detail considered</h2>
          <div className="gallery-grid">
            {GALLERY.map((item) => (
              <div key={item.src} className="gallery-item reveal">
                <AppScreenshot src={item.src} alt={item.alt} caption={item.caption} />
                <p className="gallery-caption">{item.label}</p>
              </div>
            ))}
          </div>
        </section>

        {/* ── Docs entry points ── */}
        <section className="docs-section">
          <p className="section-label reveal">Documentation</p>
          <h2 className="section-title reveal">Everything you need to get going</h2>
          <p className="section-subtitle reveal">
            From installation to advanced AI configuration, the docs cover every part of Notey.
          </p>
          <div className="docs-grid">
            {DOC_CARDS.map((card) => (
              <Link key={card.title} className={clsx('doc-card', 'reveal')} to={card.to}>
                <span className="doc-card-icon" aria-hidden="true">{card.icon}</span>
                <div className="doc-card-title">{card.title}</div>
                <p className="doc-card-desc">{card.desc}</p>
                <span className="doc-card-arrow">→</span>
              </Link>
            ))}
          </div>
        </section>

        {/* ── CTA ── */}
        <section className="cta-section">
          <h2 className="cta-title reveal">Start capturing better notes today</h2>
          <p className="cta-subtitle reveal">
            Download Notey and connect it to your Obsidian vault in minutes.
          </p>
          <div className={clsx('hero-cta-row', 'reveal')}>
            <Link className="btn-primary" to="/docs/getting-started/installation">
              Read the docs →
            </Link>
            <Link className="btn-secondary" to="https://github.com/mzbrau/notey">
              View on GitHub
            </Link>
          </div>
        </section>
      </main>
    </Layout>
  );
}
