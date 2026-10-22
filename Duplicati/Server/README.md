## Update style sheets

We use [Less CSS](https://lesscss.org) as a CSS preprocessor and its minify plugin along with it, and [Autoprefixer](https://github.com/postcss/autoprefixer) through [PostCSS](https://postcss.org) to add vendor prefixes. We also use [Stylelint](https://stylelint.io/) with a plugin for Less ([stylelint-config-standard-less](https://www.npmjs.com/package/stylelint-config-standard-less)).

Before proceeding, please make sure that [npm](https://www.npmjs.com/) is installed on your computer and available on your PATH. For install instruction, check the [official documentation](https://docs.npmjs.com/cli/v9/configuring-npm/install/).

Then, run `npm install --prefix ../../` on the current directory to install the packages.

To use Stylelint and build the CSS files, run the commands below:

```
npm run lint:style
npm run build:style:css
```

`build:style:css` compiles `webroot/ngax/less/dark.less` and `default.less` into `webroot/ngax/styles/dark.css` and `default.css`, and runs Autoprefixer on them. Commit the built files together with the `.less` changes; a CI check fails when they do not match.

Run `npm run lint:style-fix` to have Stylelint fix the errors which the linter can fix by itself. `npm run build:style` runs `lint:style-fix` and then `build:style:css`. See `package.json` on the root directory for the commands behind these scripts.

Note: there are warnings about math=always on running `lessc`, but to fix those all divisions in `.less` need to be wrapped in parentheses.
