/* oxlint-disable next/no-img-element, next/no-html-link-for-pages */
import {
  ArrowRight,
  Download,
  GitFork,
  Keyboard,
  LayoutGrid,
  MonitorUp,
  Palette,
  RefreshCw,
  ShieldCheck,
} from 'lucide-react';

const repository = 'https://github.com/kurtian/DeskMux';
const installer = `${repository}/releases/latest/download/DeskMux-Setup-x64.exe`;
const portable = `${repository}/releases/latest/download/DeskMux-portable-x64.zip`;

const features = [
  { icon: LayoutGrid, title: 'Arrange real applications', body: 'Split a monitor into panes, move focus with the keyboard, resize neighboring apps, swap positions, or zoom one pane.' },
  { icon: RefreshCw, title: 'Return to your workspace', body: 'Sessions remember application membership, pane trees, monitor placement, and recent focus. Restore missing apps when you need them.' },
  { icon: MonitorUp, title: 'Built for multiple displays', body: 'Each monitor can hold its own pane canvas. DeskMux responds to taskbars, DPI changes, and disconnected displays.' },
  { icon: Palette, title: 'Make it yours', body: 'Choose a built-in light or dark theme, follow Windows, or create a custom color palette in the manager.' },
];

function PanePreview() {
  return (
    <div className="workspace-card" aria-label="Illustration of a DeskMux workspace with three tiled applications">
      <div className="workspace-bar">
        <div className="traffic"><i /><i /><i /></div><span>DESKMUX / DESIGN</span><span className="workspace-state">3 APPS</span>
      </div>
      <div className="workspace-grid">
        <div className="mock-app mock-code">
          <div className="app-label"><span className="app-dot teal" />CODE</div>
          <div className="code-lines"><i className="w-72" /><i className="w-90" /><i className="w-55" /><i className="w-82" /><i className="w-65" /></div>
          <div className="terminal-line"><span>›</span> npm run dev <b>█</b></div>
        </div>
        <div className="mock-stack">
          <div className="mock-app mock-browser">
            <div className="app-label"><span className="app-dot violet" />BROWSER</div><div className="browser-top" /><div className="browser-content"><i /><i /><i /></div>
          </div>
          <div className="mock-app mock-notes">
            <div className="app-label"><span className="app-dot amber" />NOTES</div><p>Ship the workspace.<br />Keep the flow.</p>
          </div>
        </div>
      </div>
      <div className="command-strip">
        <span><kbd>Ctrl</kbd><b>+</b><kbd>Space</kbd></span><span className="command-path">DeskMux <b>/</b> Split right</span><span className="command-key"><kbd>\</kbd></span>
      </div>
    </div>
  );
}

export default function Home() {
  return (
    <main>
      <header className="site-header">
        <a className="brand" href="#top" aria-label="DeskMux home"><img src="/deskmux-mark.svg" alt="" width="34" height="34" /><span>DeskMux</span></a>
        <nav aria-label="Main navigation">
          <a href="#features">Features</a><a href="#how-it-works">How it works</a><a href={`${repository}#readme`}>Docs</a><a className="github-link" href={repository}><GitFork size={17} /> GitHub</a>
        </nav>
      </header>

      <section className="hero" id="top">
        <div className="hero-copy">
          <p className="eyebrow"><span /> OPEN SOURCE · WINDOWS</p>
          <h1>Your apps.<br /><em>One workspace.</em></h1>
          <p className="hero-intro">Keyboard-first sessions and panes for native Windows applications. Group the tools you use, arrange them once, and move between focused workspaces in seconds.</p>
          <div className="hero-actions">
            <a className="button primary" href={installer}><Download size={19} /> Download for Windows</a><a className="button secondary" href={repository}><GitFork size={19} /> View source</a>
          </div>
          <div className="download-meta"><span>Windows 11 / 10 · x64</span><span>Free and open source</span><a href={portable}>Portable ZIP</a></div>
        </div>
        <PanePreview />
      </section>

      <section className="principle-band" aria-label="DeskMux principles">
        <span><ShieldCheck size={18} /> Your workspace stays on your PC</span><span><Keyboard size={18} /> Keyboard first, mouse friendly</span><span><GitFork size={18} /> Source available on GitHub</span>
      </section>

      <section className="features section" id="features">
        <div className="section-heading">
          <p className="eyebrow">WINDOW MANAGEMENT WITH MEMORY</p><h2>Less arranging.<br />More working.</h2><p>DeskMux borrows the speed and structure of terminal multiplexers and applies it to the Windows applications you already use.</p>
        </div>
        <div className="feature-grid">
          {features.map(({ icon: Icon, title, body }, index) => (
            <article className="feature-card" key={title}><span className="feature-number">0{index + 1}</span><Icon aria-hidden="true" /><h3>{title}</h3><p>{body}</p></article>
          ))}
        </div>
      </section>

      <section className="flow section" id="how-it-works">
        <div className="flow-copy">
          <p className="eyebrow">A SHORT PATH TO FOCUS</p><h2>One leader key.<br />Every workspace.</h2><p>Choose your leader key, then use short commands to control sessions without reaching for the taskbar.</p><a className="text-link" href={`${repository}#keyboard-commands`}>See every keyboard command <ArrowRight size={17} /></a>
        </div>
        <ol className="steps">
          <li><span>1</span><div><h3>Create a session</h3><p>Keep coding, research, communication, or any other task in its own named workspace.</p></div></li>
          <li><span>2</span><div><h3>Add your applications</h3><p>Capture windows you already have open or launch saved applications directly into a pane.</p></div></li>
          <li><span>3</span><div><h3>Switch and resume</h3><p>DeskMux brings the selected session forward and remembers its layout for the next time you return.</p></div></li>
        </ol>
      </section>

      <section className="download-section section" id="download">
        <div><p className="eyebrow">READY WHEN YOU ARE</p><h2>Build a calmer desktop.</h2><p>Install DeskMux for the simplest setup, or download the portable edition if you prefer to keep everything in one folder.</p></div>
        <div className="download-actions"><a className="button light" href={installer}><Download size={19} /> Download installer</a><a className="button outline" href={portable}>Get portable ZIP</a><small>Installer recommended for most people.</small></div>
      </section>

      <footer>
        <a className="brand" href="#top"><img src="/deskmux-mark.svg" alt="" width="30" height="30" /><span>DeskMux</span></a><p>Open-source workspace management for Windows.</p>
        <div><a href={repository}>GitHub</a><a href={`${repository}/issues`}>Support</a><a href={`${repository}/blob/main/LICENSE`}>License</a><a href="/privacy.html">Privacy</a></div><small>© 2026 Kurtian. DeskMux is not affiliated with tmux.</small>
      </footer>
    </main>
  );
}
