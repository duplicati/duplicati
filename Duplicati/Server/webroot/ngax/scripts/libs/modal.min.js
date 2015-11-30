/*
 * @license
 * angular-modal v0.5.0
 * (c) 2013 Brian Ford http://briantford.com
 * License: MIT
 */
"use strict";function modalFactoryFactory(e,t,n,r,o,a,l){return function(c){function u(e){return p.then(function(t){v||i(t,e)})}function i(o,a){if(v=angular.element(o),0===v.length)throw new Error("The template contains no elements; you need to wrap text nodes");if(d=n.$new(),h){a||(a={}),a.$scope=d;var l=r(h,a);$&&(d[$]=l)}else if(a)for(var c in a)d[c]=a[c];return t(v)(d),e.enter(v,s)}function m(){return v?e.leave(v).then(function(){d.$destroy(),d=null,v.remove(),v=null}):o.when()}function f(){return!!v}if(!(!c.template^!c.templateUrl))throw new Error("Expected modal to have exacly one of either `template` or `templateUrl`");var p,d,h=(c.template,c.controller||null),$=c.controllerAs,s=angular.element(c.container||document.body),v=null;return p=c.template?o.when(c.template):a.get(c.templateUrl,{cache:l}).then(function(e){return e.data}),{activate:u,deactivate:m,active:f}}}angular.module("btford.modal",[]).factory("btfModal",["$animate","$compile","$rootScope","$controller","$q","$http","$templateCache",modalFactoryFactory]);