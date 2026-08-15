(function () {
  const cueBall = document.getElementById('cueBall');
  const contactDot = document.getElementById('contactDot');
  const clearButton = document.getElementById('clearButton');

  function render(state) {
    if (state.x == null || state.y == null) {
      contactDot.classList.remove('visible');
      return;
    }

    contactDot.style.left = (state.x * 100) + '%';
    contactDot.style.top = (state.y * 100) + '%';
    contactDot.classList.add('visible');
  }

  function postJson(url, body) {
    fetch(url, {
      method: 'POST',
      headers: body ? { 'Content-Type': 'application/json' } : undefined,
      body: body ? JSON.stringify(body) : undefined
    }).catch((err) => console.error('Cue ball request failed', err));
  }

  cueBall.addEventListener('click', (event) => {
    const rect = cueBall.getBoundingClientRect();
    const radius = rect.width / 2;
    const dx = event.clientX - (rect.left + radius);
    const dy = event.clientY - (rect.top + radius);
    const distance = Math.sqrt(dx * dx + dy * dy);

    // Clamp the click to inside the ball's circle so the dot never lands off the sphere.
    const scale = distance > radius ? radius / distance : 1;
    const x = (radius + dx * scale) / rect.width;
    const y = (radius + dy * scale) / rect.height;

    postJson('/overlay/api/cueball/contact', { x, y });
  });

  clearButton.addEventListener('click', () => postJson('/overlay/api/cueball/clear'));

  function connect() {
    const source = new EventSource('/overlay/api/cueball/stream');
    source.onmessage = (event) => {
      try {
        render(JSON.parse(event.data));
      } catch (err) {
        console.error('Failed to parse cue ball state', err);
      }
    };
  }

  connect();
})();
