(function () {
  // Ensure namespace exists
  if (typeof SYNO === "undefined" || !SYNO.namespace) {
    throw new Error("SYNO namespace helper not available.");
  }
  SYNO.namespace("SYNO.SDS.Duplicati");

  // Small helper bucket
  SYNO.SDS.Duplicati.Utils = SYNO.SDS.Duplicati.Utils || {
    getIframeUrl: function () {
      return "/webman/3rdparty/Duplicati/index.cgi";
    },
  };

  /**
   * Main window class referenced by config:
   * "appWindow": "SYNO.SDS.Duplicati.MainWindow"
   */
  // @require SYNO.SDS.Duplicati.MainWindow
  SYNO.SDS.Duplicati.MainWindow = Vue.extend({
    name: "SYNO.SDS.Duplicati.MainWindow",
    data: function () {
      return {
        iframeUrl: SYNO.SDS.Duplicati.Utils.getIframeUrl(),
      };
    },
    template: `
      <v-app-instance class-name="SYNO.SDS.Duplicati.MainWindow">
        <v-app-window
          ref="appWindow"
          syno-id="SYNO.SDS.Duplicati.Window"
          :resizable="true"
          :maximizable="true"
          :minimizable="true"
          width="1100"
          height="750"
        >
          <div style="height:100%; width:100%;">
            <iframe
              :src="iframeUrl"
              style="width:100%; height:100%; border:0; margin:0; padding:0;"
            ></iframe>
          </div>
        </v-app-window>
      </v-app-instance>
    `,
    methods: {
      close: function () {
        if (this.$refs && this.$refs.appWindow && this.$refs.appWindow.close) {
          this.$refs.appWindow.close();
        }
      },
    },
  });

  // @require SYNO.SDS.Duplicati.Application
  SYNO.SDS.Duplicati.Application = Vue.extend({
    name: "SYNO.SDS.Duplicati.Application",
    render: function (h) {
      // Render the window component directly
      return h(SYNO.SDS.Duplicati.MainWindow);
    },
  });
})();
