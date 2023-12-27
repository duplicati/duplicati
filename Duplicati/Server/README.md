## Update style sheets

Install [less CSS](https://lesscss.org) and minify plugin:
```
npm install less -g
npm install -g less-plugin-clean-css
```

Compile styles:
```
cd webroot/ngax
lessc less/dark.less styles/dark.css --clean-css -m=always
lessc less/default.less styles/default.css --clean-css -m=always
```

There are warnings about math=always, but to fix those all divisions in `.less` need to be wrapped in parens.