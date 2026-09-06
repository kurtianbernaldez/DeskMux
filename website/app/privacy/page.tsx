/* oxlint-disable next/no-html-link-for-pages */
import { ArrowLeft } from 'lucide-react';

export default function Privacy() {
  return (
    <main className="legal-page">
      <a className="text-link" href="/"><ArrowLeft size={17} /> Back to DeskMux</a>
      <p className="eyebrow">PRIVACY</p>
      <h1>DeskMux keeps your workspace local.</h1>
      <p>DeskMux does not require an account and does not send analytics, session information, window titles, executable paths, or diagnostic logs to Kurtian.</p>
      <h2>Information stored on your computer</h2>
      <p>DeskMux stores session names, application information, window titles, executable paths, layouts, appearance settings, and diagnostic events under your local application data folder. Portable mode stores the same information in the portable DeskMux folder.</p>
      <h2>Network access</h2>
      <p>DeskMux only accesses the network when you explicitly choose to check for an update. That request reads public release information from GitHub. Applications managed by DeskMux continue using the network according to their own behavior.</p>
      <h2>Removing your data</h2>
      <p>You can open the local data folder from the Behavior page, exit DeskMux, and delete that folder. The installer asks whether to remove local sessions and settings during uninstall.</p>
      <h2>Questions</h2>
      <p>Open an issue in the <a href="https://github.com/kurtian/DeskMux/issues">DeskMux GitHub repository</a> for privacy questions or corrections.</p>
      <small>Last updated September 6, 2026.</small>
    </main>
  );
}
