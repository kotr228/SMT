(function () {
    'use strict';

    L.TileLayer.Provider = L.TileLayer.extend({
        initialize: function (arg, options) {
            var providers = L.TileLayer.Provider.providers;

            var parts = arg.split('.');

            var providerName = parts[0];
            var variantName = parts[1];

            if (!providers[providerName]) {
                throw 'No such provider (' + providerName + ')';
            }

            var provider = {
                url: providers[providerName].url,
                options: providers[providerName].options
            };

            // overwrite values in provider from variant.
            if (variantName && 'variants' in providers[providerName]) {
                if (!(variantName in providers[providerName].variants)) {
                    throw 'No such variant of ' + providerName + ' (' + variantName + ')';
                }
                var variant = providers[providerName].variants[variantName];
                var variantOptions;
                if (typeof variant === 'string') {
                    variantOptions = {
                        variant: variant
                    };
                } else {
                    variantOptions = variant.options;
                }
                provider = {
                    url: variant.url || provider.url,
                    options: L.Util.extend({}, provider.options, variantOptions)
                };
            }

            // replace attribution placeholders with their values from toplevel provider attribution,
            // recursively
            var attributionReplacer = function (attr) {
                if (attr.indexOf('{attribution.') === -1) {
                    return attr;
                }
                return attr.replace(/\{attribution.(\w*)\}/g,
                    function (match, attributionName) {
                        return attributionReplacer(providers[attributionName].options.attribution);
                    }
                );
            };
            provider.options.attribution = attributionReplacer(provider.options.attribution);

            // Compute final options combining provider options with any user overrides
            var layerOpts = L.Util.extend({}, provider.options, options);
            L.TileLayer.prototype.initialize.call(this, provider.url, layerOpts);
        }
    });

    /**
     * Definition of providers.
     * see http://leafletjs.com/reference.html#tilelayer for options in the options map.
     */

    L.TileLayer.Provider.providers = {
        OpenStreetMap: {
            url: 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
            options: {
                maxZoom: 19,
                attribution:
                    '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
            },
            variants: {
                Mapnik: {},
                DE: {
                    url: 'https://{s}.tile.openstreetmap.de/tiles/osmde/{z}/{x}/{y}.png',
                    options: {
                        maxZoom: 18
                    }
                },
                France: {
                    url: 'https://{s}.tile.openstreetmap.fr/osmfr/{z}/{x}/{y}.png',
                    options: {
                        maxZoom: 20,
                        attribution: '&copy; OpenStreetMap France | {attribution.OpenStreetMap}'
                    }
                }
            }
        },
        Esri: {
            url: 'https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}',
            options: {
                attribution: 'Tiles &copy; Esri'
            },
            variants: {
                WorldStreetMap: {},
                WorldImagery: {
                    url: 'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',
                    options: {
                        attribution:
                            'Tiles &copy; Esri &mdash; Source: Esri, i-cubed, USDA, USGS, AEX, GeoEye, Getmapping, Aerogrid, IGN, IGP, UPR-EGP, and the GIS User Community'
                    }
                }
            }
        },
        CartoDB: {
            url: 'https://{s}.basemaps.cartocdn.com/{variant}/{z}/{x}/{y}{r}.png',
            options: {
                attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions">CARTO</a>',
                subdomains: 'abcd',
                maxZoom: 20,
                variant: 'light_all'
            },
            variants: {
                Positron: 'light_all',
                PositronNoLabels: 'light_nolabels',
                DarkMatter: 'dark_all',
                DarkMatterNoLabels: 'dark_nolabels',
                Voyager: 'rastertiles/voyager'
            }
        }
    };

    L.tileLayer.provider = function (provider, options) {
        return new L.TileLayer.Provider(provider, options);
    };

})();