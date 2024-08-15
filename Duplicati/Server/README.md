## Update style sheets

Install [Less CSS](https://lesscss.org) and minify plugin. We also use [Stylelint](https://stylelint.io/) with a plugin for Less ([stylelint-config-standard-less](https://www.npmjs.com/package/stylelint-config-standard-less)).

Before proceeding, please make sure that [npm](https://www.npmjs.com/) is installed on your computer and available on your PATH. For install instruction, check the [official documentation](https://docs.npmjs.com/cli/v9/configuring-npm/install/).

Then, run the command below on the current directory to install the packages on the project root directory:

```
npm install less less-plugin-clean-css stylelint stylelint-config-standard-less --save-dev --prefix ../../
```

To use stylelint and compile the Less files, run the commands below:

```
npx stylelint "**/less/*.less"
npx lessc webroot/ngax/less/dark.less webroot/ngax/styles/dark.css --clean-css -m=always
npx lessc webroot/ngax/less/default.less webroot/ngax/styles/default.css --clean-css -m=always
```

Add `--fix` option to have Stylelint fix some fixable errors.

Alternatively, it is possible to run those commands with `npm run-script`. See `package.json` on the root directory for available scripts.

Note: there are warnings about math=always on running `lessc`, but to fix those all divisions in `.less` need to be wrapped in parens.
