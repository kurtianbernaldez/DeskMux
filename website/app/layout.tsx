import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  metadataBase: new URL('https://deskmux.kurtian.dev'),
  title: 'DeskMux — Sessions and panes for Windows apps',
  description: 'Keyboard-first sessions and pane layouts for native Windows applications. Free and open source.',
  alternates: { canonical: '/' },
  icons: { icon: '/deskmux-mark.svg' },
  openGraph: { title: 'DeskMux — Your apps. One workspace.', description: 'Keyboard-first sessions and pane layouts for native Windows applications.', url: 'https://deskmux.kurtian.dev', siteName: 'DeskMux', type: 'website' },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
