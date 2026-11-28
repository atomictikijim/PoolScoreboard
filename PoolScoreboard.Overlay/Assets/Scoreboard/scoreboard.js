(function () {
  const BALL_COLORS = {
    1: '#F2C200', 2: '#1D4E9B', 3: '#C0392B', 4: '#6C3483',
    5: '#E07B27', 6: '#1E8449', 7: '#7B241C', 8: '#111111'
  };

  function ballColor(number) {
    const key = number > 8 ? number - 8 : number;
    return BALL_COLORS[key];
  }

  function clampByte(value) {
    return Math.max(0, Math.min(255, value));
  }

  function shade(hex, percent) {
    const num = parseInt(hex.replace('#', ''), 16);
    const amt = Math.round(2.55 * percent);
    const r = clampByte((num >> 16) + amt);
    const g = clampByte(((num >> 8) & 0x00ff) + amt);
    const b = clampByte((num & 0x0000ff) + amt);
    return '#' + (0x1000000 + r * 0x10000 + g * 0x100 + b).toString(16).slice(1);
  }

  const root = document.documentElement;
  const scoreboardRoot = document.querySelector('.scoreboard');
  const homeName = document.getElementById('homeName');
  const awayName = document.getElementById('awayName');
  const homeTeam = document.getElementById('homeTeam');
  const awayTeam = document.getElementById('awayTeam');
  const homeScore = document.getElementById('homeScore');
  const awayScore = document.getElementById('awayScore');
  const raceToLabel = document.getElementById('raceToLabel');
  const sideHome = document.getElementById('sideHome');
  const sideAway = document.getElementById('sideAway');
  const shooterPointerHome = document.getElementById('shooterPointerHome');
  const shooterPointerAway = document.getElementById('shooterPointerAway');
  const scorePill = document.getElementById('scorePill');
  const capHome = document.getElementById('capHome');
  const capAway = document.getElementById('capAway');
  const capIconHome = document.getElementById('capIconHome');
  const capIconAway = document.getElementById('capIconAway');
  const winnerBanner = document.getElementById('winnerBanner');
  const ballTracker = document.getElementById('ballTracker');
  const shotClock = document.getElementById('shotClock');
  const shotClockValue = document.getElementById('shotClockValue');
  const shotClockLineFill = document.getElementById('shotClockLineFill');

  let hasRenderedOnce = false;

  // How long a dead stream is allowed to look live before the page says so: long enough to ride out the reconnect
  // EventSource attempts by itself, short enough that nobody reads frozen numbers as a live match.
  const STALE_AFTER_MS = 4000;

  // Whether the operator asked for the cue at all, which the payload only says once a frame has arrived. Off by
  // default because a mark on the overlay is their call, not ours.
  let staleCueEnabled = false;
  let staleTimer = null;

  // The state carries the instant the shot's clock runs out, not the seconds left, so the countdown happens here
  // between frames: one frame when the clock starts, and it ticks in the OBS capture without the host sending
  // forty of them.
  let shotClockEndsAtMs = null;

  // The length the shot was given, so the line drains in proportion to it: a twenty-second shot's line runs out
  // twice as fast as a forty-second one's, and both reach empty when the clock does. An extension pushes the time
  // past the length, so the fraction is capped at a full line.
  let shotClockLengthMs = 0;

  function applyTheme(colors) {
    root.style.setProperty('--bg-home', colors.homeBackground);
    root.style.setProperty('--bg-home-light', shade(colors.homeBackground, 18));
    root.style.setProperty('--bg-home-dark', shade(colors.homeBackground, -22));
    root.style.setProperty('--bg-away', colors.awayBackground);
    root.style.setProperty('--bg-away-light', shade(colors.awayBackground, 18));
    root.style.setProperty('--bg-away-dark', shade(colors.awayBackground, -22));
    root.style.setProperty('--accent-home', colors.homeAccent);
    root.style.setProperty('--accent-away', colors.awayAccent);
    root.style.setProperty('--text-home', colors.homeText);
    root.style.setProperty('--text-away', colors.awayText);
  }

  function applyStyle(style, homeIsShooter) {
    root.style.setProperty('--radius-scale', String(style.cornerRoundness / 100));
    root.style.setProperty('--ui-scale', String(style.overallScale / 100));
    scoreboardRoot.classList.toggle('flat-finish', !style.glossyFinish);
    scoreboardRoot.classList.toggle('caps-hidden', style.endCapStyle === 'Hidden');

    const showGlow = style.shooterIndicatorStyle === 'Glow' || style.shooterIndicatorStyle === 'Both';
    const showTriangle = style.shooterIndicatorStyle === 'Triangle' || style.shooterIndicatorStyle === 'Both';
    scoreboardRoot.classList.toggle('shooter-glow', showGlow);
    shooterPointerHome.classList.toggle('visible', showTriangle && homeIsShooter);
    shooterPointerAway.classList.toggle('visible', showTriangle && !homeIsShooter);
  }

  function setStaleCue(stale) {
    document.body.classList.toggle('stale', stale);
  }

  // An error starts the clock on the cue; a frame or a reconnect inside those seconds clears it, so only a stream
  // that stays down is marked. Deliberately not a watchdog on frames: the host sends one per change rather than a
  // heartbeat, so a quiet table and a dead host look identical from here.
  function scheduleStaleCue(source) {
    clearTimeout(staleTimer);
    staleTimer = setTimeout(() => {
      if (staleCueEnabled && source.readyState !== EventSource.OPEN)
        setStaleCue(true);
    }, STALE_AFTER_MS);
  }

  function clearStaleCue() {
    clearTimeout(staleTimer);
    setStaleCue(false);
  }

  function setElementVisible(el, visible, skipAnimation) {
    if (skipAnimation) {
      el.classList.add('sb-no-transition');
      el.classList.toggle('sb-hidden', !visible);
      el.style.display = visible ? '' : 'none';
      void el.offsetHeight;
      el.classList.remove('sb-no-transition');
      return;
    }

    if (visible) {
      if (el.style.display === 'none') {
        el.style.display = '';
        void el.offsetHeight;
      }
      requestAnimationFrame(() => el.classList.remove('sb-hidden'));
    } else {
      el.classList.add('sb-hidden');
      const onEnd = (event) => {
        if (event.target !== el || event.propertyName !== 'opacity') return;
        el.removeEventListener('transitionend', onEnd);
        if (el.classList.contains('sb-hidden')) {
          el.style.display = 'none';
        }
      };
      el.addEventListener('transitionend', onEnd);
    }
  }

  function raceToText(state) {
    if (state.raceToMode === 'Split') {
      return `RACE TO ${state.home.raceToTarget} / ${state.away.raceToTarget}`;
    }
    return `RACE TO ${state.home.raceToTarget}`;
  }

  function groupBallNumbers(group) {
    if (group === 'Solids') return [1, 2, 3, 4, 5, 6, 7];
    if (group === 'Stripes') return [9, 10, 11, 12, 13, 14, 15];
    return [];
  }

  function ballElement(number, pocketed) {
    const isStripe = number > 8;
    const el = document.createElement('div');
    el.className = 'ball ' + (isStripe ? 'stripe' : 'solid') + (pocketed ? ' pocketed' : '');
    el.style.setProperty('--ball-color', ballColor(number));

    const badge = document.createElement('span');
    badge.className = 'ball-badge';
    badge.textContent = String(number);
    el.appendChild(badge);

    return el;
  }

  function renderBalls(state) {
    ballTracker.innerHTML = '';
    const pocketed = new Set(state.pocketedBalls);

    if (state.gameType === 'EightBall') {
      groupBallNumbers(state.home.ballGroup).forEach((n) =>
        ballTracker.appendChild(ballElement(n, pocketed.has(n))));
      ballTracker.appendChild(ballElement(8, pocketed.has(8)));
      groupBallNumbers(state.away.ballGroup).forEach((n) =>
        ballTracker.appendChild(ballElement(n, pocketed.has(n))));
    } else {
      const highBall = state.gameType === 'TenBall' ? 10 : 9;
      for (let n = 1; n <= highBall; n++) {
        ballTracker.appendChild(ballElement(n, pocketed.has(n)));
      }
    }
  }

  // End-cap icons come from their own endpoint — the state payload only carries a URL.
  // Only touch `src` when that reference actually changes, so a score update doesn't
  // re-request an unchanged image; a new `v` (i.e. the operator picked a new icon) does.
  function applyCapIcon(img, cap, url) {
    if (img.getAttribute('src') === url) return;
    if (url) img.src = url; else img.removeAttribute('src');
    cap.classList.toggle('has-icon', !!url);
  }

  // GameManager trims names at the single write path, so an unnamed player arrives as "" and the
  // pills and winner banner only have to pick a fallback for that case.
  function displayText(value, fallback) {
    return (value || fallback).toUpperCase();
  }

  function clockText(remainingMs) {
    const totalSeconds = Math.max(0, Math.ceil(remainingMs / 1000));
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes}:${String(seconds).padStart(2, '0')}`;
  }

  // One drawing routine for both states that show a number: a clock counting, and a clock frozen by a pause. The
  // difference is only whether `remaining` is recomputed or held.
  function drawClock(remaining) {
    shotClockValue.textContent = clockText(remaining);
    shotClock.classList.toggle('low', remaining > 0 && remaining <= 5000);
    shotClock.classList.toggle('expired', remaining <= 0);

    // The line is the same countdown measured rather than spelled out; the colours come from the two classes
    // above, so it turns red exactly when the numbers do, and stays red once the time is gone.
    const fraction = shotClockLengthMs > 0 ? Math.min(1, Math.max(0, remaining / shotClockLengthMs)) : 0;
    shotClockLineFill.style.width = (fraction * 100).toFixed(2) + '%';
  }

  function tickShotClock() {
    if (shotClockEndsAtMs === null) return;
    drawClock(shotClockEndsAtMs - Date.now());
  }

  function render(state) {
    applyTheme(state.colors);

    // A frame is proof the stream is alive, so it clears the cue and carries whether the cue is on at all.
    staleCueEnabled = state.staleCueEnabled === true;
    clearStaleCue();

    homeName.textContent = displayText(state.home.name, 'HOME');
    awayName.textContent = displayText(state.away.name, 'AWAY');
    homeTeam.textContent = (state.home.teamName || '').toUpperCase();
    awayTeam.textContent = (state.away.teamName || '').toUpperCase();
    applyCapIcon(capIconHome, capHome, state.home.endCapIconUrl);
    applyCapIcon(capIconAway, capAway, state.away.endCapIconUrl);
    homeScore.textContent = String(state.home.score);
    awayScore.textContent = String(state.away.score);
    raceToLabel.textContent = raceToText(state);

    sideHome.classList.toggle('active', state.homeIsCurrentShooter);
    sideAway.classList.toggle('active', !state.homeIsCurrentShooter);

    applyStyle(state.style, state.homeIsCurrentShooter);

    // A winner is present whenever the payload carries a winner name at all: an unnamed player
    // sends "" rather than null, and that must still show the banner (displayText supplies HOME or
    // AWAY, the same fallback the name pills use).
    const hasWinner = state.winnerName != null;
    const winnerLabel = hasWinner
      ? displayText(state.winnerName, state.winnerIsHome ? 'Home' : 'Away')
      : '';
    if (hasWinner) {
      winnerBanner.textContent = `${winnerLabel} WINS!`;
      winnerBanner.classList.toggle('winner-home', state.winnerIsHome);
      winnerBanner.classList.toggle('winner-away', !state.winnerIsHome);
    }

    // The ball tracker and winner banner are dependent overlays of the score bar — if the
    // score bar itself is hidden, they hide too, regardless of their own individual toggle.
    const scoreBarVisible = state.visibility.scoreBarVisible;
    const showBallTracker = scoreBarVisible && state.visibility.ballTrackerVisible;
    const showWinner = scoreBarVisible && hasWinner && state.visibility.winnerBannerVisible;
    const skipAnimation = !hasRenderedOnce;

    // A clock with no deadline is not a clock showing zero: a new rack and a reset both send null, and the
    // element disappears with them rather than sitting at 0:00 for the rest of the match.
    shotClockEndsAtMs = state.shotClockEndsAt ? Date.parse(state.shotClockEndsAt) : null;
    shotClockLengthMs = (state.shotClockSeconds || 0) * 1000;
    shotClock.classList.toggle('line-display', state.shotClockDisplay === 'Line');

    // A new rack leaves the clock waiting at its full length rather than starting it, so it is drawn without
    // counting: no colour change, a full line, and dimmed so a viewer cannot mistake a waiting clock for a
    // running one. Only the operator starts it.
    const clockArmed = state.shotClockArmed === true;
    shotClock.classList.toggle('idle', clockArmed);

    // A paused clock shows the time it was frozen at and does not count: the page ticks only a clock with a
    // deadline, so a paused one just stays as this frame drew it until the operator resumes. Its `paused` class
    // dims it and shows the badge.
    const clockPausedMs =
      typeof state.shotClockPausedSeconds === 'number' ? state.shotClockPausedSeconds * 1000 : null;
    const clockPaused = clockPausedMs !== null;
    shotClock.classList.toggle('paused', clockPaused);

    if (clockArmed) {
      shotClock.classList.remove('low', 'expired');
      shotClockValue.textContent = clockText(shotClockLengthMs);
      shotClockLineFill.style.width = '100%';
    } else if (clockPaused) {
      drawClock(clockPausedMs);
    } else if (shotClockEndsAtMs !== null) {
      tickShotClock();
    }

    setElementVisible(scorePill, scoreBarVisible, skipAnimation);
    setElementVisible(ballTracker, showBallTracker, skipAnimation);
    setElementVisible(winnerBanner, showWinner, skipAnimation);
    setElementVisible(shotClock, clockArmed || clockPaused || shotClockEndsAtMs !== null, skipAnimation);

    renderBalls(state);

    hasRenderedOnce = true;
  }

  function connect() {
    const source = new EventSource('/overlay/api/scoreboard/stream');
    source.onmessage = (event) => {
      try {
        render(JSON.parse(event.data));
      } catch (err) {
        console.error('Failed to parse scoreboard state', err);
      }
    };
    source.onopen = clearStaleCue;
    source.onerror = () => scheduleStaleCue(source);
  }

  // Four ticks a second: enough to land on each whole second without the display ever looking late, cheap enough
  // to run for a whole match in an OBS browser source. It does nothing at all while no clock is set.
  setInterval(tickShotClock, 250);

  connect();
})();
