/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./src/**/*.{html,ts}",
  ],
  theme: {
    extend: {
      colors: {
        bg: '#0f1117',
        surface: '#1a1d27',
        border: '#2a2d3a',
        text: '#e4e4e7',
        'text-muted': '#8b8d98',
        primary: {
          DEFAULT: '#6366f1',
          hover: '#818cf8',
        },
      }
    },
  },
  plugins: [],
}
