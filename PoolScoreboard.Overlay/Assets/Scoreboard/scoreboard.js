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
  const homeName = document.getElementById('homeName');
  const awayName = document.getElementById('awayName');
  const homeScore = document.getElementById('homeScore');
  const awayScore = document.getElementById('awayScore');
  const raceToLabel = document.getElementById('raceToLabel');
  const sideHome = document.getElementById('sideHome');
  const sideAway = document.getElementById('sideAway');
  const winnerBanner = document.getElementById('winnerBanner');
  const ballTracker = document.getElementById('ballTracker');

  function applyTheme(colors) {
    root.style.setProperty('--bg', colors.background);
    root.style.setProperty('--bg-light', shade(colors.background, 18));
    root.style.setProperty('--bg-dark', shade(colors.background, -22));
    root.style.setProperty('--accent', colors.accent);
    root.style.setProperty('--text', colors.text);
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
    const el = document.createElement('div');
    el.className = 'ball' + (pocketed ? ' pocketed' : '');
    el.style.background = ballColor(number);
    el.textContent = String(number);
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

  function render(state) {
    applyTheme(state.colors);

    homeName.textContent = (state.home.name || 'HOME').toUpperCase();
    awayName.textContent = (state.away.name || 'AWAY').toUpperCase();
    homeScore.textContent = String(state.home.score);
    awayScore.textContent = String(state.away.score);
    raceToLabel.textContent = raceToText(state);

    sideHome.classList.toggle('active', state.homeIsCurrentShooter);
    sideAway.classList.toggle('active', !state.homeIsCurrentShooter);

    if (state.winnerName) {
      winnerBanner.textContent = `${state.winnerName.toUpperCase()} WINS!`;
      winnerBanner.classList.add('visible');
    } else {
      winnerBanner.classList.remove('visible');
    }

    renderBalls(state);
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
