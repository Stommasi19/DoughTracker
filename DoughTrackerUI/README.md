# DoughTracker frontend

React, TypeScript, Vite, and plain CSS. Fonts are served locally from
`public/fonts` with their SIL OFL licenses.

Start FastAPI and PostgreSQL using the [root setup guide](../README.md), then run:

```sh
npm ci
npm run dev
```

Open <http://127.0.0.1:5173> to create a local account or sign in. Vite proxies
`/api` to `http://127.0.0.1:5083`. Set `API_PROXY_TARGET` to use another
running API instance.

```sh
npm run build
npm run lint
npm run test:auth
```

The auth check creates disposable profiles on the running API. Use a
throwaway database volume for that check.
