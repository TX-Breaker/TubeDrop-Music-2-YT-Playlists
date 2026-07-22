import { useState, useRef, useEffect, useCallback } from 'react'

// ─── Types ────────────────────────────────────────────────────────────────────

type NavScreen = 'home' | 'activity' | 'settings'
type Screen = 'home' | 'queue' | 'report' | 'activity' | 'settings'
type TrackStatus = 'queued' | 'searching' | 'matched' | 'added' | 'fallback' | 'not-found' | 'duplicate' | 'error'
type Scope = 'ytmusic' | 'youtube' | 'both'
type Privacy = 'private' | 'unlisted' | 'public'
type PlaylistMode = 'new' | 'existing'
type AuthState = 'signed-out' | 'signing-in' | 'signed-in'

interface Track {
  id: string; title: string; artist: string; duration: string
  status: TrackStatus; confidence?: number
  matchedTitle?: string; matchedChannel?: string; matchedDuration?: string
}

interface SessionTrack { id: string; title: string; artist: string; action: string; undone: boolean }

interface Session {
  id: string; date: string; playlistName: string
  added: number; uncertain: number; notFound: number
  expanded: boolean; tracks: SessionTrack[]
}

interface Toast { id: string; message: string; onUndo?: () => void; seconds: number }

// ─── Mock Data ────────────────────────────────────────────────────────────────

const TRACKS: Track[] = [
  { id: '1', title: 'So What', artist: 'Miles Davis', duration: '9:22', status: 'added', confidence: 97, matchedTitle: 'Miles Davis - So What (Official Audio)', matchedChannel: 'Miles Davis Official', matchedDuration: '9:22' },
  { id: '2', title: 'Almost Blue', artist: 'Chet Baker', duration: '4:03', status: 'matched', confidence: 87, matchedTitle: 'Chet Baker Almost Blue (Live 1988)', matchedChannel: 'JazzClassicsVault', matchedDuration: '4:01' },
  { id: '3', title: 'A Love Supreme, Pt. I', artist: 'John Coltrane', duration: '7:04', status: 'searching' },
  { id: '4', title: 'Feeling Good', artist: 'Nina Simone', duration: '2:59', status: 'added', confidence: 96, matchedTitle: 'Nina Simone - Feeling Good (Official Audio)', matchedChannel: 'Nina Simone Official', matchedDuration: '2:59' },
  { id: '5', title: 'Waltz for Debby', artist: 'Bill Evans', duration: '6:45', status: 'fallback', confidence: 71, matchedTitle: 'Bill Evans Trio - Waltz for Debby (Live)', matchedChannel: 'PianoJazzArchive', matchedDuration: '7:12' },
  { id: '6', title: "'Round Midnight", artist: 'Thelonious Monk', duration: '5:31', status: 'not-found' },
  { id: '7', title: 'Watermelon Man', artist: 'Herbie Hancock', duration: '5:47', status: 'duplicate', confidence: 99, matchedTitle: 'Herbie Hancock - Watermelon Man', matchedChannel: 'HerbieHancockVEVO', matchedDuration: '5:47' },
  { id: '8', title: 'The Köln Concert (Excerpt)', artist: 'Keith Jarrett', duration: '26:00', status: 'queued' },
  { id: '9', title: 'Come Away with Me', artist: 'Norah Jones', duration: '3:18', status: 'added', confidence: 94, matchedTitle: 'Norah Jones - Come Away With Me (Official)', matchedChannel: 'Norah Jones', matchedDuration: '3:18' },
  { id: '10', title: 'Take Five', artist: 'Dave Brubeck', duration: '5:24', status: 'error' },
]

const INITIAL_SESSIONS: Session[] = [
  {
    id: 's1', date: 'Today, 14:32', playlistName: 'Jazz Classics 2026',
    added: 24, uncertain: 3, notFound: 1, expanded: false,
    tracks: [
      { id: 't1', title: 'So What', artist: 'Miles Davis', action: 'Added to Jazz Classics 2026', undone: false },
      { id: 't2', title: 'Feeling Good', artist: 'Nina Simone', action: 'Added to Jazz Classics 2026', undone: false },
      { id: 't3', title: 'Take Five', artist: 'Dave Brubeck', action: 'Skipped — not found', undone: false },
    ],
  },
  {
    id: 's2', date: 'Jul 19, 09:11', playlistName: 'Late Night Vibes',
    added: 18, uncertain: 2, notFound: 0, expanded: false,
    tracks: [
      { id: 't4', title: 'Come Away with Me', artist: 'Norah Jones', action: 'Added to Late Night Vibes', undone: false },
      { id: 't5', title: 'Almost Blue', artist: 'Chet Baker', action: 'Added to Late Night Vibes', undone: true },
    ],
  },
  {
    id: 's3', date: 'Jul 15, 22:47', playlistName: 'Morning Grind',
    added: 31, uncertain: 1, notFound: 2, expanded: false,
    tracks: [
      { id: 't6', title: 'Watermelon Man', artist: 'Herbie Hancock', action: 'Added to Morning Grind', undone: false },
    ],
  },
]

// ─── Icons ────────────────────────────────────────────────────────────────────

const s = { fill: 'none', stroke: 'currentColor', strokeWidth: 1.7, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const }

function Ico({ d, children, cls = 'w-5 h-5', fill = false }: { d?: string; children?: React.ReactNode; cls?: string; fill?: boolean }) {
  return (
    <svg className={cls} viewBox="0 0 24 24" {...(fill ? { fill: 'currentColor' } : s)}>
      {d ? <path d={d} /> : children}
    </svg>
  )
}

const IcoHome = ({ cls }: { cls?: string }) => <Ico cls={cls}><path d="M3 9.5L12 3l9 6.5V20a1 1 0 01-1 1H4a1 1 0 01-1-1V9.5z" /><path d="M9 21V12h6v9" /></Ico>
const IcoClock = ({ cls }: { cls?: string }) => <Ico cls={cls}><circle cx="12" cy="12" r="9" /><polyline points="12 7 12 12 15 15" /></Ico>
const IcoGear = ({ cls }: { cls?: string }) => <Ico cls={cls}><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 010 2.83 2 2 0 01-2.83 0l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 012.83-2.83l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z" /></Ico>
const IcoPlay = ({ cls }: { cls?: string }) => <Ico cls={cls} fill><path d="M8 5v14l11-7z" /></Ico>
const IcoMusic = ({ cls }: { cls?: string }) => <Ico cls={cls}><path d="M9 18V5l12-2v13" /><circle cx="6" cy="18" r="3" /><circle cx="18" cy="16" r="3" /></Ico>
const IcoUpload = ({ cls }: { cls?: string }) => <Ico cls={cls}><polyline points="16 16 12 12 8 16" /><line x1="12" y1="12" x2="12" y2="21" /><path d="M20.39 18.39A5 5 0 0018 9h-1.26A8 8 0 103 16.3" /></Ico>
const IcoPause = ({ cls }: { cls?: string }) => <Ico cls={cls} fill><rect x="6" y="4" width="4" height="16" /><rect x="14" y="4" width="4" height="16" /></Ico>
const IcoX = ({ cls }: { cls?: string }) => <Ico cls={cls}><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></Ico>
const IcoChevDown = ({ cls }: { cls?: string }) => <Ico cls={cls}><polyline points="6 9 12 15 18 9" /></Ico>
const IcoChevRight = ({ cls }: { cls?: string }) => <Ico cls={cls}><polyline points="9 18 15 12 9 6" /></Ico>
const IcoExtLink = ({ cls }: { cls?: string }) => <Ico cls={cls}><path d="M18 13v6a2 2 0 01-2 2H5a2 2 0 01-2-2V8a2 2 0 012-2h6" /><polyline points="15 3 21 3 21 9" /><line x1="10" y1="14" x2="21" y2="3" /></Ico>
const IcoDownload = ({ cls }: { cls?: string }) => <Ico cls={cls}><path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4" /><polyline points="7 10 12 15 17 10" /><line x1="12" y1="15" x2="12" y2="3" /></Ico>
const IcoSearch = ({ cls }: { cls?: string }) => <Ico cls={cls}><circle cx="11" cy="11" r="8" /><line x1="21" y1="21" x2="16.65" y2="16.65" /></Ico>
const IcoShield = ({ cls }: { cls?: string }) => <Ico cls={cls}><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" /></Ico>
const IcoUndo = ({ cls }: { cls?: string }) => <Ico cls={cls}><polyline points="1 4 1 10 7 10" /><path d="M3.51 15a9 9 0 102.13-9.36L1 10" /></Ico>
const IcoGoogle = ({ cls }: { cls?: string }) => (
  <svg className={cls} viewBox="0 0 24 24">
    <path d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z" fill="#4285F4"/>
    <path d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" fill="#34A853"/>
    <path d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z" fill="#FBBC05"/>
    <path d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z" fill="#EA4335"/>
  </svg>
)

// ─── Status Chip ──────────────────────────────────────────────────────────────

function StatusChip({ status, confidence }: { status: TrackStatus; confidence?: number }) {
  const conf = confidence ?? 0
  const label =
    status === 'added' ? 'Added ✓' :
    status === 'matched' ? `Matched ${conf}%` :
    status === 'fallback' ? 'Fallback ↻' :
    status === 'not-found' ? 'Not found ⚠' :
    status === 'duplicate' ? 'Duplicate' :
    status === 'error' ? 'Error' :
    status === 'searching' ? 'Searching…' : 'Queued'

  const cls =
    status === 'added' ? 'bg-emerald-500/10 text-emerald-300' :
    status === 'matched' ? (conf >= 90 ? 'bg-emerald-500/10 text-emerald-300' : conf >= 75 ? 'bg-amber-500/10 text-amber-300' : 'bg-orange-500/10 text-orange-300') :
    status === 'fallback' ? 'bg-amber-500/10 text-amber-300' :
    status === 'not-found' ? 'bg-red-500/10 text-red-400' :
    status === 'duplicate' ? 'bg-purple-500/10 text-purple-300' :
    status === 'error' ? 'bg-red-500/10 text-red-400' :
    status === 'searching' ? 'bg-blue-500/10 text-blue-300' :
    'bg-white/5 text-zinc-500'

  return (
    <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded text-[11px] font-medium tracking-wide ${cls}`}>
      {status === 'searching' && <span className="w-1.5 h-1.5 rounded-full bg-blue-400 animate-pulse" />}
      {label}
    </span>
  )
}

// ─── Toast Container ──────────────────────────────────────────────────────────

function ToastContainer({ toasts, dismiss }: { toasts: Toast[]; dismiss: (id: string) => void }) {
  if (!toasts.length) return null
  return (
    <div className="fixed bottom-6 left-1/2 z-50 flex flex-col gap-2 pointer-events-none" style={{ transform: 'translateX(-50%)' }}>
      {toasts.map(t => (
        <div key={t.id} className="toast-in pointer-events-auto flex items-center gap-3 px-4 py-3 rounded-lg glass border border-white/10 shadow-2xl text-sm min-w-[320px]">
          <span className="flex-1 text-zinc-200">{t.message}</span>
          {t.onUndo && (
            <button
              onClick={() => { t.onUndo!(); dismiss(t.id) }}
              className="text-[#e8294c] font-semibold hover:text-[#ff3d5f] transition-colors text-xs"
            >
              Undo ({t.seconds}s)
            </button>
          )}
          <button onClick={() => dismiss(t.id)} className="text-zinc-500 hover:text-zinc-300 transition-colors">
            <IcoX cls="w-3.5 h-3.5" />
          </button>
        </div>
      ))}
    </div>
  )
}

// ─── Nav Rail ─────────────────────────────────────────────────────────────────

function NavRail({ screen, onNav, user }: { screen: Screen; onNav: (s: NavScreen) => void; user: string }) {
  const active = screen === 'queue' || screen === 'report' ? 'home' : screen as NavScreen
  const items: { id: NavScreen; label: string; icon: React.ReactNode }[] = [
    { id: 'home', label: 'Home', icon: <IcoHome /> },
    { id: 'activity', label: 'Activity', icon: <IcoClock /> },
    { id: 'settings', label: 'Settings', icon: <IcoGear /> },
  ]
  return (
    <nav className="flex flex-col w-[196px] h-full border-r shrink-0" style={{ background: '#0c0c0c', borderColor: 'rgba(255,255,255,0.06)' }}>
      {/* Logo */}
      <div className="flex items-center gap-3 px-5 pt-6 pb-8">
        <div className="w-8 h-8 rounded-lg flex items-center justify-center shrink-0" style={{ background: '#e8294c' }}>
          <IcoPlay cls="w-4 h-4 text-white" />
        </div>
        <div>
          <div className="text-[15px] font-semibold tracking-tight text-zinc-100 leading-tight">TubeDrop</div>
          <div className="text-[10px] text-zinc-500 tracking-widest uppercase leading-tight">Beta</div>
        </div>
      </div>

      {/* Nav items */}
      <div className="flex flex-col gap-0.5 px-3 flex-1">
        {items.map(item => (
          <button
            key={item.id}
            onClick={() => onNav(item.id)}
            className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-[13px] font-medium transition-colors text-left ${
              active === item.id
                ? 'text-[#e8294c]'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-white/[0.04]'
            }`}
            style={active === item.id ? { background: 'rgba(232,41,76,0.1)' } : {}}
          >
            {item.icon}
            {item.label}
          </button>
        ))}
      </div>

      {/* User card */}
      <div className="px-4 py-4 border-t" style={{ borderColor: 'rgba(255,255,255,0.06)' }}>
        <div className="flex items-center gap-2.5">
          <div className="w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold text-white shrink-0"
            style={{ background: 'linear-gradient(135deg, #7c3aed 0%, #db2777 100%)' }}>
            {user[0].toUpperCase()}
          </div>
          <div className="flex-1 min-w-0">
            <div className="text-[12px] font-medium text-zinc-200 truncate">{user}</div>
            <div className="text-[10px] text-zinc-500 truncate">Connected · YouTube</div>
          </div>
        </div>
      </div>
    </nav>
  )
}

// ─── Onboarding Screen ────────────────────────────────────────────────────────

function OnboardingScreen({ onSignIn }: { onSignIn: () => void }) {
  const [phase, setPhase] = useState<'idle' | 'loading'>('idle')

  const handleSignIn = () => {
    setPhase('loading')
    setTimeout(onSignIn, 1800)
  }

  return (
    <div className="w-full h-full flex items-center justify-center" style={{ background: '#080808' }}>
      {/* Subtle background grid */}
      <div className="absolute inset-0 opacity-[0.03]" style={{
        backgroundImage: 'linear-gradient(rgba(255,255,255,0.5) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.5) 1px, transparent 1px)',
        backgroundSize: '48px 48px',
      }} />

      <div className="relative z-10 flex flex-col items-center gap-10 max-w-[480px] w-full px-8">
        {/* Logo mark */}
        <div className="flex flex-col items-center gap-4">
          <div className="w-16 h-16 rounded-2xl flex items-center justify-center shadow-2xl" style={{ background: 'linear-gradient(135deg, #e8294c 0%, #c01f3a 100%)' }}>
            <IcoPlay cls="w-8 h-8 text-white" />
          </div>
          <div className="text-center">
            <h1 className="text-3xl font-bold tracking-tight text-zinc-100">TubeDrop</h1>
            <p className="text-[15px] text-zinc-400 mt-1.5">Drop music. Find it. Build your playlist.</p>
          </div>
        </div>

        {/* Sign-in card */}
        <div className="w-full rounded-xl border p-6 flex flex-col gap-6" style={{ background: '#141414', borderColor: 'rgba(255,255,255,0.08)' }}>
          <div className="text-[13px] text-zinc-400 font-medium uppercase tracking-widest text-center">Connect your account</div>

          {/* Google sign-in */}
          <button
            onClick={handleSignIn}
            disabled={phase === 'loading'}
            className="flex items-center justify-center gap-3 w-full py-3 px-4 rounded-lg border text-[14px] font-medium transition-all hover:border-white/20 hover:bg-white/[0.04] disabled:opacity-50 disabled:cursor-not-allowed"
            style={{ background: '#1e1e1e', borderColor: 'rgba(255,255,255,0.1)', color: '#e0e0e0' }}
          >
            {phase === 'loading' ? (
              <>
                <div className="w-5 h-5 rounded-full border-2 border-white/10 border-t-[#e8294c] spin" />
                Connecting…
              </>
            ) : (
              <>
                <IcoGoogle cls="w-5 h-5" />
                Continue with Google
              </>
            )}
          </button>

          {/* Trust bullets */}
          <div className="flex flex-col gap-2.5 pt-1 border-t" style={{ borderColor: 'rgba(255,255,255,0.06)' }}>
            {[
              { icon: <IcoShield cls="w-3.5 h-3.5 shrink-0 text-emerald-400" />, text: 'Your session stays on this PC' },
              { icon: <IcoGear cls="w-3.5 h-3.5 shrink-0 text-zinc-400" />, text: 'No API keys, no setup required' },
              { icon: <IcoUndo cls="w-3.5 h-3.5 shrink-0 text-zinc-400" />, text: 'Every action is undoable' },
            ].map((b, i) => (
              <div key={i} className="flex items-center gap-2.5 text-[12px] text-zinc-400">
                {b.icon}
                {b.text}
              </div>
            ))}
          </div>
        </div>

        <p className="text-[11px] text-zinc-600 text-center">
          By connecting, you authorize TubeDrop to manage playlists on your behalf.
          <br />Credentials are stored locally and never transmitted.
        </p>
      </div>
    </div>
  )
}

// ─── Home Screen ──────────────────────────────────────────────────────────────

interface HomeScreenProps {
  onStart: (count: number, playlistName: string) => void
}

function HomeScreen({ onStart }: HomeScreenProps) {
  const [dragOver, setDragOver] = useState(false)
  const [fileCount, setFileCount] = useState(0)
  const [playlistMode, setPlaylistMode] = useState<PlaylistMode>('new')
  const [playlistName, setPlaylistName] = useState('Jazz Selection')
  const [privacy, setPrivacy] = useState<Privacy>('private')
  const [scope, setScope] = useState<Scope>('ytmusic')
  const [matchFolder, setMatchFolder] = useState(false)
  const [folderSource, setFolderSource] = useState<'master' | 'sub'>('master')
  const dropRef = useRef<HTMLDivElement>(null)
  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault()
    setDragOver(false)
    const count = e.dataTransfer.files.length || Math.floor(Math.random() * 30) + 5
    setFileCount(count)
  }

  const handleDragOver = (e: React.DragEvent) => { e.preventDefault(); setDragOver(true) }
  const handleDragLeave = () => setDragOver(false)

  const handleBrowse = () => {
    const count = Math.floor(Math.random() * 20) + 8
    setFileCount(count)
  }

  return (
    <div className="flex flex-col h-full p-8 gap-6 overflow-y-auto">
      {/* Page title */}
      <div>
        <h2 className="text-[22px] font-semibold tracking-tight text-zinc-100">Home</h2>
        <p className="text-[13px] text-zinc-500 mt-0.5">Drop files or a folder to begin matching</p>
      </div>

      {/* Drop Zone */}
      <div
        ref={dropRef}
        onDrop={handleDrop}
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        className={`relative flex flex-col items-center justify-center rounded-2xl border-2 border-dashed transition-all cursor-pointer flex-1 min-h-[240px] max-h-[340px] ${dragOver ? 'dz-active' : 'dz-idle'}`}
        style={{
          borderColor: dragOver ? '#e8294c' : fileCount > 0 ? 'rgba(232,41,76,0.35)' : 'rgba(255,255,255,0.1)',
          background: dragOver
            ? 'radial-gradient(ellipse at center, rgba(232,41,76,0.06) 0%, transparent 70%)'
            : fileCount > 0
              ? 'radial-gradient(ellipse at center, rgba(232,41,76,0.03) 0%, transparent 70%)'
              : 'transparent',
        }}
        onClick={handleBrowse}
      >
        <div className="flex flex-col items-center gap-4 pointer-events-none select-none">
          {fileCount > 0 ? (
            <>
              <div className="w-16 h-16 rounded-2xl flex items-center justify-center" style={{ background: 'rgba(232,41,76,0.12)' }}>
                <IcoMusic cls="w-8 h-8 text-[#e8294c]" />
              </div>
              <div className="text-center">
                <div className="text-2xl font-bold text-zinc-100">{fileCount} tracks ready</div>
                <div className="text-[13px] text-zinc-500 mt-1">Click to add more, or drag another batch</div>
              </div>
            </>
          ) : (
            <>
              <div className={`w-16 h-16 rounded-2xl flex items-center justify-center ${dragOver ? '' : 'dz-idle'}`}
                style={{ background: dragOver ? 'rgba(232,41,76,0.12)' : 'rgba(255,255,255,0.04)' }}>
                <IcoUpload cls={`w-8 h-8 ${dragOver ? 'text-[#e8294c]' : 'text-zinc-500'}`} />
              </div>
              <div className="text-center">
                <div className={`text-lg font-semibold transition-colors ${dragOver ? 'text-[#e8294c]' : 'text-zinc-300'}`}>
                  {dragOver ? 'Release to load' : 'Drop songs or folders here'}
                </div>
                <div className="text-[13px] text-zinc-500 mt-1">or click to browse — MP3, FLAC, AAC, WAV supported</div>
              </div>
            </>
          )}
        </div>
      </div>

      {/* Target bar */}
      <div className="rounded-xl border p-4 flex flex-col gap-4" style={{ background: '#141414', borderColor: 'rgba(255,255,255,0.07)' }}>
        {/* Playlist mode toggle */}
        <div className="flex items-center gap-2">
          <div className="flex rounded-lg overflow-hidden border" style={{ borderColor: 'rgba(255,255,255,0.08)', background: '#0e0e0e' }}>
            {(['new', 'existing'] as PlaylistMode[]).map(m => (
              <button
                key={m}
                onClick={() => setPlaylistMode(m)}
                className={`px-4 py-1.5 text-[12px] font-medium transition-colors ${playlistMode === m ? 'text-zinc-100' : 'text-zinc-500 hover:text-zinc-300'}`}
                style={playlistMode === m ? { background: '#262626' } : {}}
              >
                {m === 'new' ? 'New playlist' : 'Add to existing'}
              </button>
            ))}
          </div>
        </div>

        {playlistMode === 'new' ? (
          <div className="flex flex-col gap-3">
            <div className="flex items-center gap-3">
            <input
              value={matchFolder ? (folderSource === 'master' ? 'Road trips' : 'Late-night drive') : playlistName}
              disabled={matchFolder}
              onChange={e => setPlaylistName(e.target.value)}
              placeholder="Playlist name…"
              className="flex-1 bg-[#1a1a1a] border rounded-lg px-3 py-2 text-[13px] text-zinc-200 placeholder-zinc-600 outline-none focus:border-[#e8294c]/40 transition-colors disabled:opacity-50"
              style={{ borderColor: 'rgba(255,255,255,0.08)' }}
            />
            {/* Privacy pills */}
            <div className="flex gap-1">
              {(['private', 'unlisted', 'public'] as Privacy[]).map(p => (
                <button
                  key={p}
                  onClick={() => setPrivacy(p)}
                  className={`px-3 py-1.5 rounded-lg text-[11px] font-medium capitalize transition-colors ${privacy === p ? 'text-[#e8294c]' : 'text-zinc-500 hover:text-zinc-300'}`}
                  style={privacy === p ? { background: 'rgba(232,41,76,0.1)' } : { background: '#1e1e1e' }}
                >
                  {p}
                </button>
              ))}
            </div>
            </div>
            <div className="flex items-center gap-3 rounded-lg border px-3 py-2.5" style={{ background: '#101010', borderColor: 'rgba(255,255,255,0.05)' }}>
              <Toggle value={matchFolder} onChange={setMatchFolder} />
              <div className="flex-1 min-w-0">
                <div className="text-[12.5px] font-medium text-zinc-200">Name playlist after the dropped folder</div>
                <div className="text-[11px] text-zinc-500">Use the folder name instead of typing one</div>
              </div>
              {matchFolder && (
                <div className="flex rounded-lg overflow-hidden border fade-in" style={{ borderColor: 'rgba(255,255,255,0.08)', background: '#0e0e0e' }}>
                  {([['master', 'Master folder', '/Road trips'], ['sub', 'Subfolder', '/Late-night drive']] as ['master' | 'sub', string, string][]).map(([id, label, hint]) => (
                    <button
                      key={id}
                      onClick={() => setFolderSource(id)}
                      className={`px-3 py-1 text-left transition-colors ${folderSource === id ? 'text-zinc-100' : 'text-zinc-500 hover:text-zinc-300'}`}
                      style={folderSource === id ? { background: '#262626' } : {}}
                    >
                      <span className="block text-[11.5px] font-medium leading-tight">{label}</span>
                      <span className="block text-[9.5px] text-zinc-600 leading-tight">{hint}</span>
                    </button>
                  ))}
                </div>
              )}
            </div>
          </div>
        ) : (
          <div className="relative">
            <IcoSearch cls="absolute left-3 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-zinc-500 pointer-events-none" />
            <input
              placeholder="Search your playlists…"
              className="w-full bg-[#1a1a1a] border rounded-lg pl-9 pr-3 py-2 text-[13px] text-zinc-200 placeholder-zinc-600 outline-none focus:border-[#e8294c]/40 transition-colors"
              style={{ borderColor: 'rgba(255,255,255,0.08)' }}
            />
          </div>
        )}

        {/* Bottom row: scope + start */}
        <div className="flex items-center justify-between pt-1 border-t" style={{ borderColor: 'rgba(255,255,255,0.05)' }}>
          {/* Scope chips */}
          <div className="flex gap-1.5 items-center">
            <span className="text-[11px] text-zinc-600 mr-1">Search in</span>
            {([['ytmusic', 'YT Music'], ['youtube', 'YouTube'], ['both', 'Both']] as [Scope, string][]).map(([id, label]) => (
              <button
                key={id}
                onClick={() => setScope(id)}
                className={`px-3 py-1 rounded-full text-[11px] font-medium transition-colors ${scope === id ? 'text-zinc-100' : 'text-zinc-500 hover:text-zinc-300'}`}
                style={scope === id ? { background: 'rgba(255,255,255,0.1)', border: '1px solid rgba(255,255,255,0.15)' } : { border: '1px solid rgba(255,255,255,0.06)' }}
              >
                {label}
              </button>
            ))}
          </div>

          {/* Start CTA */}
          <button
            onClick={() => fileCount > 0 && onStart(fileCount, (matchFolder ? (folderSource === 'master' ? 'Road trips' : 'Late-night drive') : playlistName) || 'New Playlist')}
            disabled={fileCount === 0}
            className="flex items-center gap-2 px-5 py-2.5 rounded-lg text-[13px] font-semibold transition-all disabled:opacity-30 disabled:cursor-not-allowed"
            style={{
              background: fileCount > 0 ? '#e8294c' : '#e8294c',
              color: 'white',
              opacity: fileCount === 0 ? 0.3 : 1,
            }}
          >
            Start
            {fileCount > 0 && (
              <span className="inline-flex items-center justify-center w-5 h-5 rounded-full text-[10px] font-bold" style={{ background: 'rgba(255,255,255,0.25)' }}>
                {fileCount}
              </span>
            )}
          </button>
        </div>
      </div>

      {/* Recent sessions preview */}
      <div>
        <div className="flex items-center justify-between mb-3">
          <span className="text-[12px] font-medium text-zinc-500 uppercase tracking-widest">Recent</span>
        </div>
        <div className="flex gap-3">
          {[
            { name: 'Jazz Classics 2026', count: 28, date: 'Today' },
            { name: 'Late Night Vibes', count: 20, date: 'Jul 19' },
            { name: 'Morning Grind', count: 34, date: 'Jul 15' },
          ].map((s, i) => (
            <div key={i} className="flex-1 rounded-xl border p-4 cursor-pointer hover:border-white/15 transition-colors" style={{ background: '#111111', borderColor: 'rgba(255,255,255,0.07)' }}>
              <div className="text-[13px] font-medium text-zinc-200 truncate">{s.name}</div>
              <div className="text-[11px] text-zinc-600 mt-1">{s.count} tracks · {s.date}</div>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}

// ─── Queue Screen ─────────────────────────────────────────────────────────────

function QueueScreen({ trackCount, playlistName, onDone }: { trackCount: number; playlistName: string; onDone: () => void }) {
  const tracks = TRACKS.slice(0, Math.min(trackCount, TRACKS.length))
  const [progress, setProgress] = useState(37)
  const [paused, setPaused] = useState(false)
  const [logOpen, setLogOpen] = useState(false)
  const [elapsed, setElapsed] = useState(42)

  useEffect(() => {
    if (paused) return
    const t = setInterval(() => {
      setProgress(p => { if (p >= 100) { clearInterval(t); return 100 } return p + 0.4 })
      setElapsed(e => e + 1)
    }, 200)
    return () => clearInterval(t)
  }, [paused])

  useEffect(() => {
    if (progress >= 100) setTimeout(onDone, 600)
  }, [progress, onDone])

  const added = tracks.filter(t => t.status === 'added').length
  const searching = tracks.filter(t => t.status === 'searching').length
  const errors = tracks.filter(t => t.status === 'error' || t.status === 'not-found').length

  const fmtTime = (s: number) => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`
  const eta = Math.round((elapsed / progress) * (100 - progress))

  return (
    <div className="flex flex-col h-full">
      {/* Progress header */}
      <div className="px-8 pt-6 pb-5 border-b shrink-0" style={{ borderColor: 'rgba(255,255,255,0.06)' }}>
        <div className="flex items-center justify-between mb-4">
          <div>
            <h2 className="text-[18px] font-semibold text-zinc-100">{playlistName}</h2>
            <div className="text-[12px] text-zinc-500 mt-0.5">
              {added} added · {searching} processing · {errors} issues · {fmtTime(elapsed)} elapsed · {eta > 0 ? `~${fmtTime(eta)} left` : 'almost done'}
            </div>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => setPaused(p => !p)}
              className="flex items-center gap-2 px-4 py-2 rounded-lg text-[12px] font-medium transition-colors hover:bg-white/[0.06] text-zinc-300"
              style={{ border: '1px solid rgba(255,255,255,0.1)' }}
            >
              {paused ? <IcoPlay cls="w-4 h-4" /> : <IcoPause cls="w-4 h-4" />}
              {paused ? 'Resume' : 'Pause'}
            </button>
            <button className="flex items-center gap-2 px-4 py-2 rounded-lg text-[12px] font-medium text-zinc-400 hover:text-red-400 transition-colors"
              style={{ border: '1px solid rgba(255,255,255,0.08)' }}>
              <IcoX cls="w-4 h-4" />
              Cancel
            </button>
          </div>
        </div>

        {/* Progress bar */}
        <div className="relative h-1.5 rounded-full overflow-hidden" style={{ background: 'rgba(255,255,255,0.08)' }}>
          <div
            className="absolute inset-y-0 left-0 rounded-full transition-all duration-300"
            style={{ width: `${progress}%`, background: 'linear-gradient(90deg, #e8294c, #ff3d5f)' }}
          />
        </div>
        <div className="flex justify-between mt-1.5">
          <span className="text-[10px] text-zinc-600">{Math.round(progress)}%</span>
          <span className="text-[10px] text-zinc-600">{tracks.length} tracks total</span>
        </div>
      </div>

      {/* Track list */}
      <div className="flex-1 overflow-y-auto">
        {tracks.map((track, i) => (
          <div
            key={track.id}
            className={`flex items-center gap-4 px-8 py-3.5 border-b hover:bg-white/[0.02] transition-colors ${track.status === 'searching' ? 'row-searching' : ''}`}
            style={{ borderColor: 'rgba(255,255,255,0.04)', animationDelay: `${i * 0.1}s` }}
          >
            {/* Track number / cover */}
            <div className="w-8 h-8 rounded flex items-center justify-center text-[11px] text-zinc-600 shrink-0"
              style={{ background: '#1a1a1a' }}>
              {i + 1}
            </div>

            {/* Info */}
            <div className="flex-1 min-w-0">
              <div className="text-[13px] font-medium text-zinc-200 truncate">{track.title}</div>
              <div className="text-[11px] text-zinc-500 truncate">{track.artist}</div>
            </div>

            {/* Duration */}
            <div className="text-[11px] text-zinc-600 font-mono w-10 text-right shrink-0">{track.duration}</div>

            {/* Matched video */}
            {track.matchedTitle && (
              <div className="flex-1 min-w-0 hidden xl:block">
                <div className="text-[11px] text-zinc-400 truncate">{track.matchedTitle}</div>
                <div className="text-[10px] text-zinc-600 truncate">{track.matchedChannel}</div>
              </div>
            )}

            {/* Status */}
            <div className="shrink-0 w-36 flex justify-end">
              <StatusChip status={track.status} confidence={track.confidence} />
            </div>
          </div>
        ))}
      </div>

      {/* Log drawer */}
      <div className="shrink-0 border-t" style={{ borderColor: 'rgba(255,255,255,0.06)' }}>
        <button
          onClick={() => setLogOpen(o => !o)}
          className="flex items-center gap-2 w-full px-8 py-3 text-[11px] text-zinc-500 hover:text-zinc-300 transition-colors"
        >
          {logOpen ? <IcoChevDown cls="w-3.5 h-3.5" /> : <IcoChevRight cls="w-3.5 h-3.5" />}
          Live log
        </button>
        {logOpen && (
          <div className="px-8 pb-4 font-mono text-[10px] text-zinc-600 max-h-28 overflow-y-auto space-y-0.5" style={{ background: '#0a0a0a' }}>
            {[
              '[14:33:21] Searching: Miles Davis – So What → matched (97%)',
              '[14:33:22] Searching: Chet Baker – Almost Blue → matched (87%)',
              '[14:33:23] Searching: John Coltrane – A Love Supreme, Pt. I…',
              '[14:33:19] Added: Nina Simone – Feeling Good to playlist',
              '[14:33:18] Warning: Bill Evans – Waltz for Debby — low confidence, using fallback',
              '[14:33:17] Error: \'Round Midnight by Thelonious Monk — no results found',
            ].map((line, i) => (
              <div key={i} className={line.includes('Error') ? 'text-red-500/70' : line.includes('Warning') ? 'text-amber-500/70' : ''}>{line}</div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

// ─── Report Screen ────────────────────────────────────────────────────────────

type ReportFilter = 'all' | 'added' | 'uncertain' | 'not-found' | 'duplicates' | 'errors'

function ReportScreen({ playlistName, addToast }: { playlistName: string; addToast: (msg: string) => void }) {
  const [filter, setFilter] = useState<ReportFilter>('all')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editQuery, setEditQuery] = useState('')

  const summaryCards: { id: ReportFilter; label: string; count: number; color: string }[] = [
    { id: 'added', label: 'Added', count: 6, color: 'text-emerald-300' },
    { id: 'uncertain', label: 'Uncertain', count: 2, color: 'text-amber-300' },
    { id: 'not-found', label: 'Not found', count: 1, color: 'text-red-400' },
    { id: 'duplicates', label: 'Duplicates', count: 1, color: 'text-purple-300' },
    { id: 'errors', label: 'Errors', count: 1, color: 'text-red-400' },
  ]

  const filteredTracks = TRACKS.filter(t => {
    if (filter === 'all') return true
    if (filter === 'added') return t.status === 'added'
    if (filter === 'uncertain') return t.status === 'matched' || t.status === 'fallback'
    if (filter === 'not-found') return t.status === 'not-found'
    if (filter === 'duplicates') return t.status === 'duplicate'
    if (filter === 'errors') return t.status === 'error'
    return true
  })

  return (
    <div className="flex flex-col h-full overflow-y-auto">
      <div className="px-8 pt-6 pb-4 shrink-0">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-[18px] font-semibold text-zinc-100">Session Report</h2>
            <p className="text-[13px] text-zinc-500 mt-0.5">{playlistName} · {TRACKS.length} tracks processed</p>
          </div>
          <button
            onClick={() => addToast('Exported report as CSV')}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-[12px] font-medium text-zinc-300 hover:text-zinc-100 transition-colors"
            style={{ border: '1px solid rgba(255,255,255,0.1)', background: '#161616' }}
          >
            <IcoDownload cls="w-3.5 h-3.5" />
            Export CSV
          </button>
        </div>

        {/* Summary cards */}
        <div className="flex gap-3 mt-5">
          <button
            onClick={() => setFilter('all')}
            className={`flex-1 rounded-xl p-4 border text-left transition-all hover:border-white/15 ${filter === 'all' ? 'border-white/15' : 'border-white/[0.06]'}`}
            style={{ background: filter === 'all' ? '#1e1e1e' : '#111111' }}
          >
            <div className="text-2xl font-bold text-zinc-100">{TRACKS.length}</div>
            <div className="text-[11px] text-zinc-500 mt-0.5">Total</div>
          </button>
          {summaryCards.map(card => (
            <button
              key={card.id}
              onClick={() => setFilter(filter === card.id ? 'all' : card.id)}
              className={`flex-1 rounded-xl p-4 border text-left transition-all hover:border-white/15 ${filter === card.id ? 'border-white/15' : 'border-white/[0.06]'}`}
              style={{ background: filter === card.id ? '#1e1e1e' : '#111111' }}
            >
              <div className={`text-2xl font-bold ${card.color}`}>{card.count}</div>
              <div className="text-[11px] text-zinc-500 mt-0.5">{card.label}</div>
            </button>
          ))}
        </div>
      </div>

      {/* Table */}
      <div className="flex-1 px-8 pb-8">
        <div className="rounded-xl border overflow-hidden" style={{ borderColor: 'rgba(255,255,255,0.07)' }}>
          {/* Table header */}
          <div className="flex items-center gap-4 px-4 py-3 border-b text-[10px] font-semibold tracking-widest uppercase text-zinc-600"
            style={{ background: '#0e0e0e', borderColor: 'rgba(255,255,255,0.06)' }}>
            <div className="w-5" />
            <div className="flex-1">Track</div>
            <div className="flex-1 hidden lg:block">Matched video</div>
            <div className="w-20 text-right hidden md:block">Conf.</div>
            <div className="w-20 text-right">Status</div>
            <div className="w-20 text-right">Actions</div>
          </div>

          {filteredTracks.length === 0 && (
            <div className="flex flex-col items-center justify-center py-16 text-center">
              <div className="text-3xl mb-3">🎉</div>
              <div className="text-[15px] font-medium text-zinc-300">Everything matched perfectly</div>
              <div className="text-[12px] text-zinc-600 mt-1">No issues found in this session</div>
            </div>
          )}

          {filteredTracks.map((track) => (
            <div key={track.id}>
              <div
                className="flex items-center gap-4 px-4 py-3.5 hover:bg-white/[0.02] transition-colors border-b"
                style={{ borderColor: 'rgba(255,255,255,0.04)' }}
              >
                <div className="w-5 text-[11px] text-zinc-600 text-center">{filteredTracks.indexOf(track) + 1}</div>
                <div className="flex-1 min-w-0">
                  <div className="text-[13px] font-medium text-zinc-200 truncate">{track.title}</div>
                  <div className="text-[11px] text-zinc-500">{track.artist} · {track.duration}</div>
                </div>
                <div className="flex-1 min-w-0 hidden lg:block">
                  {track.matchedTitle ? (
                    <>
                      <div className="text-[12px] text-zinc-300 truncate">{track.matchedTitle}</div>
                      <div className="text-[10px] text-zinc-600">{track.matchedChannel} · {track.matchedDuration}</div>
                    </>
                  ) : (
                    <span className="text-[11px] text-zinc-600">—</span>
                  )}
                </div>
                <div className="w-20 text-right hidden md:block">
                  {track.confidence != null ? (
                    <span className={`text-[12px] font-mono font-semibold ${track.confidence >= 90 ? 'text-emerald-400' : track.confidence >= 75 ? 'text-amber-400' : 'text-orange-400'}`}>
                      {track.confidence}%
                    </span>
                  ) : <span className="text-zinc-600 text-[11px]">—</span>}
                </div>
                <div className="w-20 flex justify-end">
                  <StatusChip status={track.status} confidence={track.confidence} />
                </div>
                <div className="w-20 flex justify-end items-center gap-2">
                  {track.matchedTitle && (
                    <button className="text-zinc-600 hover:text-zinc-300 transition-colors" title="Open on YouTube">
                      <IcoExtLink cls="w-3.5 h-3.5" />
                    </button>
                  )}
                  <button
                    onClick={() => { setEditingId(editingId === track.id ? null : track.id); setEditQuery(track.title + ' ' + track.artist) }}
                    className="text-zinc-600 hover:text-zinc-300 transition-colors text-[10px]"
                    title="Edit query"
                  >
                    <IcoSearch cls="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>

              {/* Inline edit row */}
              {editingId === track.id && (
                <div className="flex items-center gap-3 px-4 py-3 border-b fade-in" style={{ background: '#181818', borderColor: 'rgba(255,255,255,0.05)' }}>
                  <div className="w-5" />
                  <input
                    autoFocus
                    value={editQuery}
                    onChange={e => setEditQuery(e.target.value)}
                    className="flex-1 bg-[#222] border rounded-lg px-3 py-1.5 text-[12px] text-zinc-200 outline-none focus:border-[#e8294c]/40"
                    style={{ borderColor: 'rgba(255,255,255,0.1)' }}
                  />
                  <button
                    onClick={() => setEditingId(null)}
                    className="px-3 py-1.5 rounded-lg text-[11px] font-medium text-white transition-colors"
                    style={{ background: '#e8294c' }}
                  >
                    Retry
                  </button>
                  <button onClick={() => setEditingId(null)} className="text-zinc-600 hover:text-zinc-400 text-[11px]">Cancel</button>
                </div>
              )}
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}

// ─── Activity Screen ──────────────────────────────────────────────────────────

function ActivityScreen({ addToast }: { addToast: (msg: string, onUndo?: () => void) => void }) {
  const [sessions, setSessions] = useState<Session[]>(INITIAL_SESSIONS)
  const [confirmId, setConfirmId] = useState<string | null>(null)

  const toggleExpand = (id: string) => {
    setSessions(ss => ss.map(s => s.id === id ? { ...s, expanded: !s.expanded } : s))
  }

  const undoTrack = (sessionId: string, trackId: string) => {
    setSessions(ss => ss.map(s => s.id === sessionId
      ? { ...s, tracks: s.tracks.map(t => t.id === trackId ? { ...t, undone: true } : t) }
      : s))
    addToast('Removed 1 track from playlist', () => {
      setSessions(ss => ss.map(s => s.id === sessionId
        ? { ...s, tracks: s.tracks.map(t => t.id === trackId ? { ...t, undone: false } : t) }
        : s))
    })
  }

  const undoSession = (sessionId: string) => {
    const session = sessions.find(s => s.id === sessionId)
    if (!session) return
    setSessions(ss => ss.map(s => s.id === sessionId
      ? { ...s, tracks: s.tracks.map(t => ({ ...t, undone: true })) }
      : s))
    addToast(`Removed ${session.added} tracks from ${session.playlistName}`)
    setConfirmId(null)
  }

  return (
    <div className="flex flex-col h-full overflow-y-auto">
      <div className="px-8 pt-6 pb-4 shrink-0">
        <h2 className="text-[22px] font-semibold tracking-tight text-zinc-100">Activity</h2>
        <p className="text-[13px] text-zinc-500 mt-0.5">Review and undo past sessions</p>
      </div>

      <div className="px-8 pb-8 flex flex-col gap-3">
        {sessions.map(session => (
          <div key={session.id} className="rounded-xl border overflow-hidden" style={{ borderColor: 'rgba(255,255,255,0.07)', background: '#111111' }}>
            {/* Session header */}
            <div className="flex items-center gap-4 px-5 py-4">
              <button
                onClick={() => toggleExpand(session.id)}
                className="flex items-center gap-3 flex-1 text-left"
              >
                {session.expanded ? <IcoChevDown cls="w-4 h-4 text-zinc-500 shrink-0" /> : <IcoChevRight cls="w-4 h-4 text-zinc-500 shrink-0" />}
                <div className="flex-1 min-w-0">
                  <div className="text-[14px] font-semibold text-zinc-200">{session.playlistName}</div>
                  <div className="text-[11px] text-zinc-500 mt-0.5">{session.date} · {session.added} added · {session.uncertain} uncertain · {session.notFound} not found</div>
                </div>
              </button>

              {/* Undo session */}
              {confirmId === session.id ? (
                <div className="flex items-center gap-2">
                  <span className="text-[12px] text-zinc-400">Undo {session.added} items?</span>
                  <button
                    onClick={() => undoSession(session.id)}
                    className="px-3 py-1.5 rounded-lg text-[11px] font-medium text-white transition-colors"
                    style={{ background: '#e8294c' }}
                  >
                    Confirm
                  </button>
                  <button onClick={() => setConfirmId(null)} className="text-[11px] text-zinc-500 hover:text-zinc-300">Cancel</button>
                </div>
              ) : (
                <button
                  onClick={() => setConfirmId(session.id)}
                  className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11px] font-medium text-zinc-400 hover:text-zinc-200 transition-colors"
                  style={{ border: '1px solid rgba(255,255,255,0.08)' }}
                >
                  <IcoUndo cls="w-3.5 h-3.5" />
                  Undo session
                </button>
              )}
            </div>

            {/* Track list */}
            {session.expanded && (
              <div className="border-t" style={{ borderColor: 'rgba(255,255,255,0.05)' }}>
                {session.tracks.map((track) => (
                  <div
                    key={track.id}
                    className={`flex items-center gap-4 px-5 py-3 border-b hover:bg-white/[0.02] transition-colors ${track.undone ? 'opacity-40' : ''}`}
                    style={{ borderColor: 'rgba(255,255,255,0.04)', paddingLeft: '3.25rem' }}
                  >
                    <div className="flex-1 min-w-0">
                      <span className="text-[13px] font-medium text-zinc-300">{track.title}</span>
                      <span className="text-[11px] text-zinc-600 ml-2">{track.artist}</span>
                    </div>
                    <div className="text-[11px] text-zinc-600">{track.action}</div>
                    {!track.undone ? (
                      <button
                        onClick={() => undoTrack(session.id, track.id)}
                        className="flex items-center gap-1 px-2.5 py-1 rounded text-[10px] text-zinc-500 hover:text-zinc-200 transition-colors"
                        style={{ border: '1px solid rgba(255,255,255,0.07)' }}
                      >
                        <IcoUndo cls="w-3 h-3" />
                        Undo
                      </button>
                    ) : (
                      <span className="text-[10px] text-zinc-600 italic">Undone</span>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  )
}

// ─── Settings Screen ──────────────────────────────────────────────────────────

function SettingsScreen() {
  const [threshold, setThreshold] = useState(82)
  const [aggressive, setAggressive] = useState(false)
  const [language, setLanguage] = useState<'en' | 'it'>('en')
  const [scope, setScope] = useState<Scope>('ytmusic')
  const [privacy, setPrivacy] = useState<Privacy>('private')
  const [apiKey, setApiKey] = useState('')
  const [apiVisible, setApiVisible] = useState(false)

  const confidenceColor = threshold >= 90 ? 'text-emerald-400' : threshold >= 75 ? 'text-amber-400' : 'text-orange-400'

  return (
    <div className="flex flex-col h-full overflow-y-auto">
      <div className="px-8 pt-6 pb-4 shrink-0">
        <h2 className="text-[22px] font-semibold tracking-tight text-zinc-100">Settings</h2>
        <p className="text-[13px] text-zinc-500 mt-0.5">Customize how TubeDrop works</p>
      </div>

      <div className="px-8 pb-8 flex flex-col gap-4 max-w-2xl">
        {/* General */}
        <SettingsGroup title="General">
          <SettingsRow label="Language" description="Interface language">
            <div className="flex gap-1">
              {(['en', 'it'] as const).map(l => (
                <button key={l} onClick={() => setLanguage(l)}
                  className={`px-3 py-1 rounded text-[12px] font-medium uppercase transition-colors ${language === l ? 'text-white' : 'text-zinc-500 hover:text-zinc-300'}`}
                  style={language === l ? { background: '#e8294c' } : { background: '#1e1e1e' }}>
                  {l}
                </button>
              ))}
            </div>
          </SettingsRow>
          <SettingsRow label="Default privacy" description="For newly created playlists">
            <div className="flex gap-1">
              {(['private', 'unlisted', 'public'] as Privacy[]).map(p => (
                <button key={p} onClick={() => setPrivacy(p)}
                  className={`px-3 py-1 rounded text-[11px] font-medium capitalize transition-colors ${privacy === p ? 'text-white' : 'text-zinc-500 hover:text-zinc-300'}`}
                  style={privacy === p ? { background: '#262626', border: '1px solid rgba(255,255,255,0.15)' } : { background: '#1a1a1a', border: '1px solid rgba(255,255,255,0.06)' }}>
                  {p}
                </button>
              ))}
            </div>
          </SettingsRow>
        </SettingsGroup>

        {/* Search & matching */}
        <SettingsGroup title="Search & Matching">
          <SettingsRow label="Default scope" description="Where to search for tracks">
            <div className="flex gap-1">
              {([['ytmusic', 'YT Music'], ['youtube', 'YouTube'], ['both', 'Both']] as [Scope, string][]).map(([id, label]) => (
                <button key={id} onClick={() => setScope(id)}
                  className={`px-3 py-1 rounded text-[11px] font-medium transition-colors ${scope === id ? 'text-white' : 'text-zinc-500 hover:text-zinc-300'}`}
                  style={scope === id ? { background: '#262626', border: '1px solid rgba(255,255,255,0.15)' } : { background: '#1a1a1a', border: '1px solid rgba(255,255,255,0.06)' }}>
                  {label}
                </button>
              ))}
            </div>
          </SettingsRow>
          <SettingsRow label="Match threshold" description={`Minimum confidence to auto-add — currently ${threshold}%`}>
            <div className="flex items-center gap-3 w-48">
              <input type="range" min={50} max={99} value={threshold} onChange={e => setThreshold(+e.target.value)} className="flex-1 h-1" />
              <span className={`text-[13px] font-semibold font-mono w-10 text-right ${confidenceColor}`}>{threshold}%</span>
            </div>
          </SettingsRow>
          <SettingsRow label="Aggressive mode" description="Accept lower-confidence matches automatically">
            <Toggle value={aggressive} onChange={setAggressive} />
          </SettingsRow>
        </SettingsGroup>

        {/* Fallback */}
        <SettingsGroup title="Fallback & Recognition">
          <SettingsRow label="Local recognition module" description="Identify tracks by audio fingerprint">
            <div className="flex items-center gap-2.5">
              <div className="w-2 h-2 rounded-full bg-amber-400" />
              <span className="text-[12px] text-amber-300 font-medium">Downloading…</span>
              <div className="w-24 h-1 rounded-full overflow-hidden" style={{ background: 'rgba(255,255,255,0.08)' }}>
                <div className="h-full rounded-full" style={{ width: '64%', background: '#e8294c' }} />
              </div>
              <span className="text-[11px] text-zinc-500">64%</span>
            </div>
          </SettingsRow>
          <SettingsRow label="Cloud API key" description="Optional AcoustID / MusicBrainz key for enhanced recognition">
            <div className="flex items-center gap-2">
              <input
                type={apiVisible ? 'text' : 'password'}
                value={apiKey}
                onChange={e => setApiKey(e.target.value)}
                placeholder="Paste API key…"
                className="bg-[#1a1a1a] border rounded px-3 py-1.5 text-[12px] text-zinc-300 placeholder-zinc-600 outline-none focus:border-[#e8294c]/40 w-40"
                style={{ borderColor: 'rgba(255,255,255,0.08)' }}
              />
              <button onClick={() => setApiVisible(v => !v)} className="text-[10px] text-zinc-500 hover:text-zinc-300">
                {apiVisible ? 'Hide' : 'Show'}
              </button>
            </div>
          </SettingsRow>
        </SettingsGroup>

        {/* Updates */}
        <SettingsGroup title="Updates">
          <SettingsRow label="Release channel" description="Which builds to receive">
            <div className="flex gap-1">
              {['Stable', 'Beta'].map(c => (
                <button key={c} className={`px-3 py-1 rounded text-[11px] font-medium transition-colors ${c === 'Stable' ? 'text-white' : 'text-zinc-500 hover:text-zinc-300'}`}
                  style={c === 'Stable' ? { background: '#262626', border: '1px solid rgba(255,255,255,0.15)' } : { background: '#1a1a1a', border: '1px solid rgba(255,255,255,0.06)' }}>
                  {c}
                </button>
              ))}
            </div>
          </SettingsRow>
          <SettingsRow label="Version" description="TubeDrop 0.9.4 — up to date">
            <button className="px-3 py-1.5 rounded text-[11px] font-medium text-zinc-400 hover:text-zinc-200 transition-colors"
              style={{ border: '1px solid rgba(255,255,255,0.08)', background: '#161616' }}>
              Check now
            </button>
          </SettingsRow>
        </SettingsGroup>

        {/* About */}
        <div className="rounded-xl border p-4 text-[11px] text-zinc-600" style={{ borderColor: 'rgba(255,255,255,0.06)', background: '#0e0e0e' }}>
          TubeDrop 0.9.4-beta · MIT License · Not affiliated with YouTube or Google
        </div>
      </div>
    </div>
  )
}

function SettingsGroup({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="text-[11px] font-semibold text-zinc-500 uppercase tracking-widest mb-2">{title}</div>
      <div className="rounded-xl border overflow-hidden" style={{ borderColor: 'rgba(255,255,255,0.07)', background: '#111111' }}>
        {children}
      </div>
    </div>
  )
}

function SettingsRow({ label, description, children }: { label: string; description: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-6 px-5 py-4 border-b last:border-b-0"
      style={{ borderColor: 'rgba(255,255,255,0.05)' }}>
      <div className="min-w-0">
        <div className="text-[13px] font-medium text-zinc-200">{label}</div>
        <div className="text-[11px] text-zinc-500 mt-0.5">{description}</div>
      </div>
      <div className="shrink-0">{children}</div>
    </div>
  )
}

function Toggle({ value, onChange }: { value: boolean; onChange: (v: boolean) => void }) {
  return (
    <button
      onClick={() => onChange(!value)}
      className="w-10 h-5 rounded-full relative transition-colors"
      style={{ background: value ? '#e8294c' : 'rgba(255,255,255,0.12)' }}
    >
      <div className="absolute top-0.5 w-4 h-4 rounded-full transition-all"
        style={{ background: 'white', left: value ? '22px' : '2px', boxShadow: '0 1px 4px rgba(0,0,0,0.4)' }} />
    </button>
  )
}

// ─── Banner ───────────────────────────────────────────────────────────────────

function SessionBanner({ onDismiss }: { onDismiss: () => void }) {
  return (
    <div className="flex items-center gap-3 px-5 py-2.5 text-[12px]" style={{ background: 'rgba(245,158,11,0.12)', borderBottom: '1px solid rgba(245,158,11,0.2)' }}>
      <span className="w-1.5 h-1.5 rounded-full bg-amber-400 shrink-0" />
      <span className="flex-1 text-amber-200">Your YouTube session expired — playlists cannot be updated until you reconnect.</span>
      <button className="text-amber-300 font-semibold hover:text-amber-100 underline underline-offset-2">Sign in again</button>
      <button onClick={onDismiss} className="text-amber-600 hover:text-amber-400 ml-1"><IcoX cls="w-3.5 h-3.5" /></button>
    </div>
  )
}

// ─── App ──────────────────────────────────────────────────────────────────────

let toastId = 0

export default function App() {
  const [auth, setAuth] = useState<AuthState>('signed-out')
  const [screen, setScreen] = useState<Screen>('home')
  const [queueMeta, setQueueMeta] = useState({ count: 0, name: '' })
  const [toasts, setToasts] = useState<Toast[]>([])
  const [banner, setBanner] = useState(false)

  const addToast = useCallback((message: string, onUndo?: () => void) => {
    const id = String(++toastId)
    setToasts(ts => [...ts, { id, message, onUndo, seconds: 8 }])
    const timer = setInterval(() => {
      setToasts(ts => ts.map(t => t.id === id ? { ...t, seconds: t.seconds - 1 } : t).filter(t => t.seconds > 0))
    }, 1000)
    setTimeout(() => { clearInterval(timer); setToasts(ts => ts.filter(t => t.id !== id)) }, 8000)
  }, [])

  const dismissToast = useCallback((id: string) => {
    setToasts(ts => ts.filter(t => t.id !== id))
  }, [])

  const handleSignIn = () => { setAuth('signed-in'); setBanner(false) }

  const handleStart = (count: number, name: string) => {
    setQueueMeta({ count, name })
    setScreen('queue')
  }

  const handleDone = () => {
    setScreen('report')
    addToast(`Processing complete — ${queueMeta.name} is ready`)
  }

  if (auth !== 'signed-in') {
    return (
      <>
        <OnboardingScreen onSignIn={handleSignIn} />
        <ToastContainer toasts={toasts} dismiss={dismissToast} />
      </>
    )
  }

  return (
    <div className="flex flex-col w-full h-full" style={{ background: '#080808' }}>
      {/* Session expired banner */}
      {banner && <SessionBanner onDismiss={() => setBanner(false)} />}

      <div className="flex flex-1 min-h-0">
        <NavRail
          screen={screen}
          onNav={(s) => setScreen(s)}
          user="you@example.com"
        />

        {/* Main content */}
        <main className="flex-1 min-w-0 h-full overflow-hidden" style={{ background: '#0c0c0c' }}>
          {/* Subtle noise overlay for Mica-like depth */}
          <div className="absolute inset-0 pointer-events-none opacity-[0.018]" style={{
            backgroundImage: `url("data:image/svg+xml,%3Csvg viewBox='0 0 200 200' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='4' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)'/%3E%3C/svg%3E")`,
            backgroundSize: '200px 200px',
          }} />

          <div className="relative z-10 h-full">
            {screen === 'home' && <HomeScreen onStart={handleStart} />}
            {screen === 'queue' && <QueueScreen trackCount={queueMeta.count} playlistName={queueMeta.name} onDone={handleDone} />}
            {screen === 'report' && <ReportScreen playlistName={queueMeta.name || 'Jazz Classics 2026'} addToast={addToast} />}
            {screen === 'activity' && <ActivityScreen addToast={addToast} />}
            {screen === 'settings' && <SettingsScreen />}
          </div>
        </main>
      </div>

      <ToastContainer toasts={toasts} dismiss={dismissToast} />
    </div>
  )
}
