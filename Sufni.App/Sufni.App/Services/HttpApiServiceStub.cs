using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform;
using Sufni.App.Models;
using Sufni.App.Models.Telemetry;

namespace Sufni.App.Services;

public class HttpApiServiceStub : IHttpApiService
{
    private static readonly Guid CalibrationMethodFractionId = Guid.Parse("230e04a092ce42189a3c23bf3cde2b05");
    private static readonly Guid CalibrationMethodPercentageId = Guid.Parse("c619045af435427797cb1e2c1fddcfeb");
    private static readonly Guid CalibrationMethodLinearId = Guid.Parse("3e799d5a5652430e900c06a3277ab1dc");
    private static readonly Guid CalibrationMethodLinearPotmeterId = Guid.Parse("347cf9a82df54ef8b63936b36357f08f");
    private static readonly Guid CalibrationMethodIsoscelesId = Guid.Parse("12f4a1b922f74524abcbdaa99a5c1c3a");
    private static readonly Guid CalibrationMethodTriangleId = Guid.Parse("9a27abc4125148a2b64989fb315ca2de");
    private static readonly Guid LinkageId1 = Guid.Parse("aa230fd54c864b3b99c81252dfea0055");
    private static readonly Guid LinkageId2 = Guid.Parse("b75d069643a0462980c3490559bd47d4");
    private static readonly Guid CalibrationMezzerId = Guid.Parse("5d58e84b38474e59846b8088c1213961");
    private static readonly Guid CalibrationMaraId = Guid.Parse("06e8142aff2b438a956c98ebb6be131b");
    private static readonly Guid CalibrationMaraRockerId = Guid.Parse("a8455d4d2d6347b0998b58461ba43d98");
    private static readonly Guid SetupClashId = Guid.Parse("6afdbe77bff1429da05c3442d59c4461");
    private static readonly Guid SetupClashNewId = Guid.Parse("8b5b3f2ed8404fb6ace97e155d40e6e8");
    private static readonly Guid SetupOneSuspensionId = Guid.Parse("484f618cd3bd48828e991d55d54003f5");

    private static readonly byte[] SessionData;

    static HttpApiServiceStub()
    {
        using var assetLoader = AssetLoader.Open(new Uri("avares://Sufni.App/Assets/sample.psst"));
        using var binaryReader = new BinaryReader(assetLoader);
        SessionData = binaryReader.ReadBytes((int)assetLoader.Length);

        Sessions =
        [
            new Session(id: new Guid("396d9b75-8adb-44b9-b265-bcabeb031b26"), name: "session 1", description: "Test session #1", setup: SetupClashNewId, timestamp: 1686998748)
            {
                ProcessedData = SessionData,
            },
            new Session(id: new Guid("f2e2300f-ff25-47f1-85e8-836704a84984"), name: "session 2", description: "Test session #2", setup: SetupClashNewId, timestamp: 1682943649)
            {
                ProcessedData = SessionData,
            },
            new Session(id: new Guid("eea9800b-89b0-4369-a017-7788ec507f98"), name: "session 3", description: "Test session #3", setup: SetupClashNewId, timestamp: 1682760595)
        ];
    }

    private static readonly List<Linkage> Linkages = new()
    {
        new(LinkageId1, (string)"Clash 2022 (dynamic)", 63.5, 180.0, 65.0,(string?)"# created with http://www.graphreader.com/\n# from https://www.pinkbike.com/forum/listcomments/?threadid=209001&pagenum=64#commentid7107387\n#      https://ep1.pinkbike.org/p5pb22577901/p5pb22577901.jpg\n0,3.083\n1,3.076\n2,3.07\n3,3.064\n4,3.058\n5,3.052\n6,3.045\n7,3.039\n8,3.033\n9,3.027\n10,3.021\n11,3.014\n12,3.008\n13,3.002\n14,2.996\n15,2.99\n16,2.983\n17,2.977\n18,2.971\n19,2.965\n20,2.959\n21,2.953\n22,2.947\n23,2.941\n24,2.935\n25,2.929\n26,2.923\n27,2.917\n28,2.911\n29,2.906\n30,2.9\n31,2.894\n32,2.888\n33,2.882\n34,2.877\n35,2.872\n36,2.866\n37,2.861\n38,2.856\n39,2.85\n40,2.845\n41,2.84\n42,2.834\n43,2.829\n44,2.824\n45,2.818\n46,2.813\n47,2.808\n48,2.802\n49,2.797\n50,2.792\n51,2.786\n52,2.781\n53,2.776\n54,2.77\n55,2.765\n56,2.76\n57,2.754\n58,2.749\n59,2.744\n60,2.739\n61,2.734\n62,2.729\n63,2.725\n64,2.72\n65,2.716\n66,2.711\n67,2.707\n68,2.702\n69,2.698\n70,2.693\n71,2.688\n72,2.684\n73,2.679\n74,2.675\n75,2.67\n76,2.666\n77,2.661\n78,2.657\n79,2.652\n80,2.648\n81,2.643\n82,2.639\n83,2.634\n84,2.63\n85,2.625\n86,2.621\n87,2.616\n88,2.612\n89,2.607\n90,2.603\n91,2.598\n92,2.594\n93,2.589\n94,2.585\n95,2.58\n96,2.576\n97,2.571\n98,2.567\n99,2.562\n100,2.558\n101,2.553\n102,2.549\n103,2.545\n104,2.54\n105,2.536\n106,2.532\n107,2.528\n108,2.524\n109,2.519\n110,2.515\n111,2.511\n112,2.507\n113,2.503\n114,2.498\n115,2.494\n116,2.49\n117,2.486\n118,2.481\n119,2.477\n120,2.473\n121,2.469\n122,2.465\n123,2.46\n124,2.457\n125,2.453\n126,2.449\n127,2.445\n128,2.441\n129,2.437\n130,2.433\n131,2.429\n132,2.425\n133,2.422\n134,2.418\n135,2.414\n136,2.41\n137,2.406\n138,2.403\n139,2.399\n140,2.395\n141,2.392\n142,2.388\n143,2.385\n144,2.381\n145,2.378\n146,2.374\n147,2.371\n148,2.367\n149,2.363\n150,2.36\n151,2.356\n152,2.353\n153,2.349\n154,2.346\n155,2.342\n156,2.339\n157,2.335\n158,2.332\n159,2.328\n160,2.325\n161,2.321\n162,2.318\n163,2.314\n164,2.311\n165,2.307\n166,2.304\n167,2.3\n168,2.297\n169,2.293\n170,2.29"),
        new(LinkageId2, (string)"Clash 2022 (sensitive)", 63.5, 180.0, 65.0,(string?)"# created with http://www.graphreader.com/\n# from https://www.pinkbike.com/forum/listcomments/?threadid=209001&pagenum=64#commentid7107387\n#      https://ep1.pinkbike.org/p5pb22577901/p5pb22577901.jpg\n0,3.18\n1,3.173\n2,3.165\n3,3.157\n4,3.149\n5,3.142\n6,3.134\n7,3.126\n8,3.119\n9,3.113\n10,3.106\n11,3.1\n12,3.093\n13,3.087\n14,3.08\n15,3.073\n16,3.067\n17,3.06\n18,3.053\n19,3.047\n20,3.04\n21,3.033\n22,3.027\n23,3.02\n24,3.014\n25,3.008\n26,3.002\n27,2.995\n28,2.989\n29,2.983\n30,2.977\n31,2.971\n32,2.965\n33,2.958\n34,2.952\n35,2.946\n36,2.94\n37,2.934\n38,2.928\n39,2.922\n40,2.916\n41,2.91\n42,2.905\n43,2.899\n44,2.893\n45,2.888\n46,2.882\n47,2.876\n48,2.871\n49,2.865\n50,2.859\n51,2.854\n52,2.849\n53,2.844\n54,2.838\n55,2.833\n56,2.828\n57,2.823\n58,2.817\n59,2.812\n60,2.807\n61,2.801\n62,2.796\n63,2.791\n64,2.786\n65,2.78\n66,2.775\n67,2.77\n68,2.766\n69,2.761\n70,2.756\n71,2.751\n72,2.746\n73,2.741\n74,2.736\n75,2.731\n76,2.726\n77,2.722\n78,2.717\n79,2.712\n80,2.707\n81,2.703\n82,2.698\n83,2.694\n84,2.689\n85,2.685\n86,2.68\n87,2.676\n88,2.671\n89,2.667\n90,2.662\n91,2.658\n92,2.653\n93,2.649\n94,2.644\n95,2.64\n96,2.635\n97,2.631\n98,2.626\n99,2.622\n100,2.618\n101,2.613\n102,2.609\n103,2.605\n104,2.601\n105,2.597\n106,2.593\n107,2.589\n108,2.585\n109,2.581\n110,2.577\n111,2.573\n112,2.569\n113,2.565\n114,2.561\n115,2.557\n116,2.553\n117,2.549\n118,2.544\n119,2.54\n120,2.536\n121,2.533\n122,2.529\n123,2.525\n124,2.522\n125,2.518\n126,2.514\n127,2.511\n128,2.507\n129,2.503\n130,2.499\n131,2.496\n132,2.492\n133,2.488\n134,2.485\n135,2.481\n136,2.478\n137,2.475\n138,2.471\n139,2.468\n140,2.465\n141,2.462\n142,2.458\n143,2.455\n144,2.452\n145,2.448\n146,2.445\n147,2.442\n148,2.439\n149,2.436\n150,2.433\n151,2.43\n152,2.427\n153,2.424\n154,2.421\n155,2.418\n156,2.415\n157,2.411\n158,2.408\n159,2.405\n160,2.402\n161,2.4\n162,2.397\n163,2.395\n164,2.392\n165,2.389\n166,2.387\n167,2.384\n168,2.381\n169,2.379\n170,2.376\n171,2.374\n172,2.371\n173,2.368\n174,2.366"),
    };

    private static readonly List<Calibration> Calibrations = new()
    {
        new (CalibrationMezzerId,
            (string)"Clash - Mezzer Pro",
            CalibrationMethodIsoscelesId,
            new Dictionary<string, double>()
            {
                {"arm", 134.9375},
                {"max", 234.15625},
            }),
        new(CalibrationMaraId,
            (string)"Clash - Mara Pro",
            CalibrationMethodIsoscelesId,
            new Dictionary<string, double>()
            {
                {"arm", 63.5},
                {"max", 119.0625},
            }),
        new(CalibrationMaraRockerId,
            (string)"Clash - Mara Pro (rocker link)",
            CalibrationMethodTriangleId,
            new Dictionary<string, double>()
            {
                {"arm1", 98.9},
                {"arm2", 202.8},
                {"max", 230},
            }),
    };

    private static readonly List<Setup> Setups = new()
    {
        new(SetupClashId, "Clash (Manitou,sensitive}", LinkageId2, CalibrationMezzerId, CalibrationMaraId),
        new(SetupClashNewId, "Clash (new)", LinkageId2, CalibrationMezzerId, CalibrationMaraRockerId),
        new(SetupOneSuspensionId, "Setup with one Calibration", LinkageId2, CalibrationMaraId, null),
    };

    private static readonly List<Board> Boards = new()
    {
        new("0000000000000000", SetupClashId),
        new("0000000000000001", SetupClashNewId),
        new("0011223344556677", SetupOneSuspensionId),
        new("0000000000000003", null),
    };

    private static readonly List<CalibrationMethod> CalibrationMethods = new()
    {
        new(CalibrationMethodFractionId,
            "fraction",
            "Sample is in fraction of maximum suspension stroke.",
            new CalibrationMethodProperties(
                new List<string>(),
                new Dictionary<string, string>(),
                "sample * MAX_STROKE")),
        new(CalibrationMethodPercentageId,
            "percentage",
            "Sample is in percentage of maximum suspension stroke.",
            new CalibrationMethodProperties(
                new List<string>(),
                new Dictionary<string, string>()
                {
                    {"factor", "MAX_STROKE / 100.0"}
                },
                "sample * factor")),
        new(CalibrationMethodLinearId,
            "linear",
            "Sample is linearly distributed within a given range.",
            new CalibrationMethodProperties(
                new List<string>()
                {
                    "min_measurement",
                    "max_measurement"
                },
                new Dictionary<string, string>()
                {
                    {"factor", "MAX_STROKE / (max_measurement - min_measurement)"}
                },
                "(sample - min_measurement) * factor")),
        new(CalibrationMethod.LinearPotmeterId,
            "linear-potmeter",
            "Sample is the ADC value read from a linear potentiometer.",
            new CalibrationMethodProperties(
                [
                    "stroke",
                    "resolution"
                ],
                new Dictionary<string, string>()
                {
                    {"factor", "stroke / (2^resolution)"}
                },
                "sample * factor")),
        new(CalibrationMethodIsoscelesId,
            "as5600-isosceles-triangle",
            "Triangle setup with the sensor between the base and leg.",
            new CalibrationMethodProperties(
                new List<string>()
                {
                    "arm",
                    "max"
                },
                new Dictionary<string, string>()
                {
                    {"start_angle", "acos(max / 2.0 / arm)"},
                    {"factor", "2.0 * pi / 4096"},
                    {"dbl_arm", "2.0 * arm"},
                },
                "max - (dbl_arm * cos((factor*sample) + start_angle))")),
        new(CalibrationMethodTriangleId,
            "as5600-triangle",
            "Triangle setup with the sensor between two known sides.",
            new CalibrationMethodProperties(
                new List<string>()
                {
                    "arm1",
                    "arm2",
                    "max"
                },
                new Dictionary<string, string>()
                {
                    {"start_angle", "acos((arm1^2+arm2^2-max^2)/(2*arm1*arm2))"},
                    {"factor", "2.0 * pi / 4096"},
                    {"arms_sqr_sum", "arm1^2 + arm2^2"},
                    {"dbl_arm1_arm2", "2 * arm1 * arm2"},
                },
                "max - sqrt(arms_sqr_sum - dbl_arm1_arm2 * cos(start_angle-(factor*sample)))")),
    };

    private static readonly List<Session> Sessions;

    public Task<string> RefreshTokensAsync(string url, string refreshToken)
    {
        return Task.FromResult(refreshToken);
    }

    public Task<string> RegisterAsync(string url, string username, string password)
    {
        return Task.FromResult("MOCK_TOKEN");
    }

    public Task UnregisterAsync(string refreshToken)
    {
        return Task.CompletedTask;
    }

    public Task<SynchronizationData> PullSyncAsync(int since = 0)
    {
        return Task.FromResult(new SynchronizationData
        {
            Boards = Boards,
            CalibrationMethods = CalibrationMethods,
            Calibrations = Calibrations,
            Linkages = Linkages,
            Setups = Setups,
            Sessions = Sessions
        });
    }

    public Task PushSyncAsync(SynchronizationData syncData)
    {
        return Task.CompletedTask;
    }

    public Task<List<Guid>> GetIncompleteSessionIdsAsync()
    {
        return Task.FromResult(Sessions.Where(s => s.ProcessedData is null).Select(s => s.Id).ToList());
    }

    public Task<byte[]?> GetSessionPsstAsync(Guid id)
    {
        return Task.FromResult(Sessions.First(s => s.Id == id).ProcessedData ?? null);
    }

    public Task PatchSessionPsstAsync(Guid id, byte[] data)
    {
        var s = Sessions.First(s => s.Id == id);
        s.ProcessedData = data;

        return Task.CompletedTask;
    }
}