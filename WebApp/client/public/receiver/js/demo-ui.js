const messages = [
  ['WoolWatcher', 'Camera 03 has the best view tonight.', '#ffba2e'],
  ['FarmHand_7', 'Did that sheep just look at us? 👀', '#62d6ff'],
  ['NightOwl', 'The pasture is unusually quiet.', '#b18cff'],
  ['HayThere', 'Try switching to the south crossing!', '#70e3a6'],
  ['ShepherdBot', 'All camera systems are standing by.', '#ff6f83'],
];
const list = document.getElementById('chatMessages');
const form = document.getElementById('chatForm');
const input = document.getElementById('chatInput');

function addMessage(name, text, color) {
  const row = document.createElement('div');
  row.className = 'chat-message';
  const user = document.createElement('strong');
  const content = document.createElement('span');
  const time = document.createElement('time');
  user.textContent = name;
  content.textContent = text;
  time.textContent = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  row.style.setProperty('--chat-color', color);
  row.append(user, content, time);
  list.append(row);
  list.scrollTop = list.scrollHeight;
}

messages.forEach(([name, text, color]) => addMessage(name, text, color));
form.addEventListener('submit', event => {
  event.preventDefault();
  const text = input.value.trim();
  if (!text) return;
  addMessage('You', text, '#ffba2e');
  input.value = '';
  input.focus();
});
