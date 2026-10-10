using System.Text.Json.Nodes;

namespace AegiNext.Application.Tests;

internal static class VersionElevenProjectJsonFixture
{
    private const string JSON = """
        {
          "version": 11,
          "id": "10000000-0000-0000-0000-000000000001",
          "name": "Version 11 karaoke compatibility fixture",
          "width": 1920,
          "height": 1080,
          "frameRate": {
            "numerator": 30,
            "denominator": 1
          },
          "referenceWhiteNits": 203,
          "media": null,
          "assets": [],
          "tracks": [
            {
              "id": "20000000-0000-0000-0000-000000000001",
              "name": "Track",
              "defaultStyle": null,
              "stylePresetId": null,
              "stylePresetName": null,
              "autoApplyStyle": true
            }
          ],
          "subtitles": [
            {
              "id": "30000000-0000-0000-0000-000000000001",
              "start": {
                "numerator": 0,
                "denominator": 1
              },
              "end": {
                "numerator": 6,
                "denominator": 1
              },
              "text": "Aé😀👩‍💻Z",
              "styleName": "Default",
              "stylePresetId": null,
              "style": {
                "fontFamily": "sans-serif",
                "fontAssetId": null,
                "fontSize": 64,
                "letterSpacing": 0,
                "wrapMode": "GRAPHEME",
                "fill": {
                  "red": 1,
                  "green": 1,
                  "blue": 1,
                  "alpha": 1
                },
                "fillBlur": 0,
                "stroke": {
                  "red": 0,
                  "green": 0,
                  "blue": 0,
                  "alpha": 1
                },
                "strokeWidth": 2,
                "strokeBlur": 0,
                "bold": false,
                "italic": false,
                "underline": false,
                "strikethrough": false,
                "alignment": "BOTTOM_CENTER",
                "margins": {
                  "left": 40,
                  "right": 40,
                  "vertical": 40
                },
                "position": null,
                "lineHeight": 1.2,
                "shadowOffset": {
                  "x": 2,
                  "y": 2
                },
                "shadowBlur": 2,
                "shadowColor": {
                  "red": 0,
                  "green": 0,
                  "blue": 0,
                  "alpha": 0.6
                }
              },
              "inlineSpans": [],
              "karaokeStyleSpans": [
                {
                  "utf16Start": 0,
                  "utf16Length": 5,
                  "activeStyle": {
                    "fill": {
                      "red": 4.123456789123,
                      "green": 0.25,
                      "blue": 1,
                      "alpha": 0.625
                    },
                    "stroke": null,
                    "strokeWidth": null,
                    "fillBlur": null,
                    "strokeBlur": null,
                    "shadowOffset": {
                      "x": 3.123456789123,
                      "y": -2.234567891234
                    },
                    "shadowBlur": null,
                    "shadowColor": null
                  },
                  "inactiveStyle": {
                    "fill": {
                      "red": 0,
                      "green": 0,
                      "blue": 0,
                      "alpha": 1
                    },
                    "stroke": null,
                    "strokeWidth": 0,
                    "fillBlur": null,
                    "strokeBlur": null,
                    "shadowOffset": null,
                    "shadowBlur": null,
                    "shadowColor": null
                  }
                },
                {
                  "utf16Start": 5,
                  "utf16Length": 5,
                  "activeStyle": null,
                  "inactiveStyle": {
                    "fill": {
                      "red": 1,
                      "green": 0,
                      "blue": 0,
                      "alpha": 0
                    },
                    "stroke": null,
                    "strokeWidth": null,
                    "fillBlur": null,
                    "strokeBlur": null,
                    "shadowOffset": null,
                    "shadowBlur": null,
                    "shadowColor": null
                  }
                }
              ],
              "karaoke": [
                {
                  "utf16Start": 0,
                  "utf16Length": 5,
                  "start": {
                    "numerator": 1,
                    "denominator": 3
                  },
                  "end": {
                    "numerator": 4,
                    "denominator": 3
                  },
                  "highlightColor": {
                    "red": 4.123456789123,
                    "green": 0.25,
                    "blue": 1,
                    "alpha": 0.625
                  },
                  "id": "40000000-0000-0000-0000-000000000001",
                  "highlightKind": "SWEEP"
                },
                {
                  "utf16Start": 10,
                  "utf16Length": 1,
                  "start": {
                    "numerator": 7,
                    "denominator": 3
                  },
                  "end": {
                    "numerator": 8,
                    "denominator": 3
                  },
                  "highlightColor": {
                    "red": 0,
                    "green": 1,
                    "blue": 0,
                    "alpha": 1
                  },
                  "id": "40000000-0000-0000-0000-000000000002",
                  "highlightKind": "OUTLINE_STEP"
                }
              ],
              "karaokeStyle": null,
              "inactiveKaraoke": [
                {
                  "utf16Start": 5,
                  "utf16Length": 5,
                  "start": {
                    "numerator": 13,
                    "denominator": 7
                  },
                  "end": {
                    "numerator": 29,
                    "denominator": 7
                  },
                  "highlightColor": {
                    "red": 1,
                    "green": 0,
                    "blue": 0,
                    "alpha": 0.5
                  },
                  "id": "40000000-0000-0000-0000-000000000003",
                  "highlightKind": "STEP"
                }
              ]
            }
          ],
          "layers": [
            {
              "id": "50000000-0000-0000-0000-000000000001",
              "trackId": "20000000-0000-0000-0000-000000000001",
              "name": "Clip",
              "kind": "SUBTITLE",
              "start": {
                "numerator": 0,
                "denominator": 1
              },
              "end": {
                "numerator": 6,
                "denominator": 1
              },
              "animationOffset": {
                "numerator": 0,
                "denominator": 1
              },
              "transform": {
                "position": {
                  "x": 0,
                  "y": 0
                },
                "scale": {
                  "x": 1,
                  "y": 1
                },
                "pivot": {
                  "x": 0,
                  "y": 0
                },
                "rotation": 0
              },
              "opacity": 1,
              "blend": "NORMAL",
              "fill": {
                "red": 1,
                "green": 1,
                "blue": 1,
                "alpha": 1
              },
              "stroke": {
                "red": 0,
                "green": 0,
                "blue": 0,
                "alpha": 1
              },
              "strokeWidth": 0,
              "blur": 0,
              "subtitleId": "30000000-0000-0000-0000-000000000001",
              "shape": null,
              "image": null,
              "mask": null,
              "motionPath": null,
              "tracks": [
                {
                  "target": {
                    "property": "OPACITY",
                    "nodeId": null
                  },
                  "keyframes": [
                    {
                      "time": {
                        "numerator": 0,
                        "denominator": 1
                      },
                      "value": 1,
                      "interpolation": "LINEAR",
                      "curveStart": 0,
                      "curveEnd": 1,
                      "exponent": 1,
                      "componentCurves": []
                    }
                  ],
                  "initialValue": null,
                  "transforms": []
                }
              ]
            }
          ],
          "presets": [],
          "timelineViewState": {
            "collapsedAnimationRows": [],
            "collapsedTrackIds": []
          }
        }
        """;

    internal static JsonObject Create(bool includeRangeStyles)
    {
        var document = JsonNode.Parse(JSON)!.AsObject();
        if (!includeRangeStyles)
        {
            document["subtitles"]![0]!.AsObject().Remove("karaokeStyleSpans");
        }
        return document;
    }
}
