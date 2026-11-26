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

  let hasRenderedOnce = false;

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

  // Operator text is free-form, so a name typed as spaces only has to fall back exactly like an
  // empty one — otherwise the name pills go blank and the winner banner reads " WINS!".
  function displayText(value, fallback) {
    const trimmed = (value || '').trim();
    return (trimmed === '' ? fallback : trimmed).toUpperCase();
  }

  function render(state) {
    applyTheme(state.colors);

    homeName.textContent = displayText(state.home.name, 'HOME');
    awayName.textContent = displayText(state.away.name, 'AWAY');
    homeTeam.textContent = (state.home.teamName || '').trim().toUpperCase();
    awayTeam.textContent = (state.away.teamName || '').trim().toUpperCase();
    applyCapIcon(capIconHome, capHome, state.home.endCapIconUrl);
    applyCapIcon(capIconAway, capAway, state.away.endCapIconUrl);
    homeScore.textContent = String(state.home.score);
    awayScore.textContent = String(state.away.score);
    raceToLabel.textContent = raceToText(state);

    sideHome.classList.toggle('active', state.homeIsCurrentShooter);
    sideAway.classList.toggle('active', !state.homeIsCurrentShooter);

    applyStyle(state.style, state.homeIsCurrentShooter);

    // A winner is present whenever the payload carries a winner name at all: an unnamed player
    // sends "" rather than null, and that must still show the banner (the name pills fall back to
    // HOME/AWAY the same way). A name of only spaces is treated as unnamed by displayText.
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

    setElementVisible(scorePill, scoreBarVisible, skipAnimation);
    setElementVisible(ballTracker, showBallTracker, skipAnimation);
    setElementVisible(winnerBanner, showWinner, skipAnimation);

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
  }

  connect();
})();
