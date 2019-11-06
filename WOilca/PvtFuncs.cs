using System;

namespace W.Oilca
{
    public static partial class PVT
    {
        #region Misc
        /// <summary>
        /// Усреднённая молярная масса воздуха ~= 28.98 г/моль
        /// </summary>
        const double AirMolarMass = 28.98;

        public static double APIcalc(this Context ctx) => 141.5 / ctx[Arg.GAMMA_O] - 131.5;

        public static (double Q_oil_rc, double Q_wat_rc, double Q_gas_rc)
            Vol_Rates(this Context ctx, double Q_liq_sc, double Watercut_sc)
        {
            // Code from BegsBrill.Calc
            var q_osc = Q_liq_sc * (1 - Watercut_sc);
            var q_wsc = Q_liq_sc * Watercut_sc;
            var q_gsc = ctx[PVT.Arg.Rsb] * q_osc;
            double Q_o = q_osc * ctx[PVT.Prm.Bo];
            double Q_w = q_wsc * ctx[PVT.Prm.Bw];
            double q_gas_free_sc = (q_gsc - ctx[PVT.Prm.Rs] * q_osc);
            double Q_g = ctx[PVT.Prm.Bg] * q_gas_free_sc;
            return (Q_o, Q_w, Q_g);
        }
        #endregion

        #region Liquid
        public static double Liq_Density(this Context ctx, double waterVolumeFraction)
        {
            var oilDensity = ctx[Prm.Rho_o]; // oilSim.CalcDensity(P, T);
            var watDensity = ctx[Prm.Rho_w]; // waterSim.CalcDensity(P, T);
            return oilDensity * (1 - waterVolumeFraction) + watDensity * waterVolumeFraction;
        }

        public static double Liq_Viscosity(this Context ctx, double waterVolumeFraction)
        {
            var oilViscosity = ctx[Prm.Mu_o]; // oilSim.CalcDensity(P, T);
            var watViscosity = ctx[Prm.Mu_w]; // waterSim.CalcDensity(P, T);
            return oilViscosity * (1 - waterVolumeFraction) + watViscosity * waterVolumeFraction;
        }

        public static double Liq_LiquidGasSurfaceTension(this Context ctx, double waterVolumeFraction)
        {
            var oilTension = ctx[Prm.Sigma_og];//oilSim.CalcOilGasSurfaceTension(P, T);
            var watTension = ctx[Prm.Sigma_wg];//waterSim.CalcWaterGasSurfaceTension(P, T);
            return oilTension * (1 - waterVolumeFraction) + watTension * waterVolumeFraction;
        }
        #endregion

        #region Mixture
        public static double Mix_Density(this Context ctx, double waterVolumeFraction, double liquidVolumeFraction, double gasVolumeFraction)
        {
            var liqDensity = ctx.Liq_Density(waterVolumeFraction); // liquidSim.CalcDensity(waterVolumeFraction, P, T);
            var gasDensity = ctx[Prm.Rho_g]; // gasSim.CalcDensity(P, T);
            return liqDensity * liquidVolumeFraction + gasDensity * gasVolumeFraction;
        }

        public static double Mix_Viscosity(this Context ctx, double waterVolumeFraction, double liquidVolumeFraction, double gasVolumeFraction)
        {
            var liqViscosity = ctx.Liq_Viscosity(waterVolumeFraction);// liquidSim.CalcViscosity(waterVolumeFraction, P, T);
            var gasViscosity = ctx[Prm.Mu_g]; // gasSim.CalcViscosity(P, T);
            return liqViscosity * liquidVolumeFraction + gasViscosity * gasVolumeFraction;
        }
        #endregion

        #region BubblePointPressure
        public static double Pb_STANDING_1947(Context ctx)
        {
            var T = ctx[Prm.T];
            var gamma_o = ctx[Arg.GAMMA_O];
            var Rsb = ctx[Arg.Rsb];
            var gamma_g = ctx[Arg.GAMMA_G];

            double yg = 1.225 + 0.00164 * T - 1.769 / gamma_o;
            double pb = 0.5197 * U.Pow(Rsb / gamma_g, 0.83) * Math.Pow(10, yg);

            return pb;
        }

        public static double Pb_VASQUEZ_BEGGS_1980(Context ctx)
        {
            double C1, C2, C3, C4;

            var gamma_o = ctx[Arg.GAMMA_O];
            {
                if (U.isGE(gamma_o, 0.876))
                {
                    C1 = 7.803 * 1.0e-4;
                    C2 = 0.9143;
                    C3 = 2022.19;
                    C4 = 1879.28;
                }
                else
                {
                    C1 = 3.204 * 1.0e-4;
                    C2 = 0.8425;
                    C3 = 1881.24;
                    C4 = 1748.29;
                }
            }
            var T = ctx[Prm.T];

            double A = C1 * ctx[Arg.GAMMA_G_CORR] * Math.Exp(C3 / (gamma_o * T) - C4 / T);
            double pb = 0.001 * U.Pow(ctx[Prm.Rs] / A, C2);
            return pb;
        }

        public static double Pb_GLASO_1980(Context ctx)
        {
            // FIXME: not black-oil case
            double a = 4.087 * U.Pow(ctx[Prm.Rs] / ctx[Arg.GAMMA_G], 0.816);
            double pb_ = a * U.Pow(U.Kelv2Fahr(ctx[Prm.T]), 0.172) / U.Pow(ctx.APIcalc(), 0.989);

            double logPb_ = U.log10(pb_);

            double b = 1.7447 * logPb_;
            double c = 0.30218 * logPb_ * logPb_;

            double e = -0.3946 + b - c;
            double pb = U.Pow(10, e);

            return pb;
        }

        public static double Pb_AL_MARHOUN_1988(Context ctx)
        {
            double _pb;
            {
                var b = U.Pow(ctx[Arg.GAMMA_G], -1.877840);
                var c = U.Pow(ctx[Arg.GAMMA_O], 3.14370);
                var e = 5.38088 * 1.0e-3;

                _pb = 0.006895 * e * b * c;
            }

            double a = U.Pow(ctx[Prm.Rs] / 0.1801, 0.715082);
            double d = U.Pow(1.8 * ctx[Prm.T], 1.32657);

            return _pb * a * d;
        }
        #endregion

        #region Solution_GOR
        public static double Rs_LASATER_1958(Context ctx)
        {
            var P = ctx[Prm.P];
            var Pb = ctx[Prm.Pb];
            var gamma_g = ctx[Arg.GAMMA_G];
            var T = ctx[Prm.T];

            var Pb_ = P < Pb ? P : Pb;
            var Kpb = Pb_ * gamma_g / T;

            var yg = U.isGE(Kpb, 0.0125)
                ? U.Pow(31.8 * Kpb - 0.236, 0.281)
                : 0.359 * Math.Log(387.0 * Kpb + 0.476);

            if (yg > 1.0)
                yg = 1.0;
            if (yg < 0.0)
                yg = 0.0;

            double _rs;
            {
                var gamma_o = ctx[Arg.GAMMA_O];
                double Mo;
                if (gamma_o < 0.825)
                {
                    Mo = 73.11 * U.Pow(ctx.APIcalc(), -1.562);
                }
                else Mo = 1945.0 - 1415.0 / gamma_o;
                _rs = 23633.0 * gamma_o / Mo;
            }

            var rs = _rs * (yg / (1 - yg));
            return rs;
        }

        public static double Gamma_g_corr_Calc(Context.Root ctx)
        {
            double gamma_g_corr = ctx[Arg.GAMMA_G];
            double gamma_g_sep = ctx[Arg.GAMMA_G_SEP];

            if (!U.isZero(gamma_g_sep))
            {
                double a = 5.912 * 1.0e-5;
                double api = ctx.APIcalc();
                double b = U.Kelv2Fahr(ctx[Arg.T_SEP]);
                double c = U.log10(1.264 * ctx[Arg.P_SEP]);

                double g = 1 + a * api * b * c;
                if (!U.isZero(g))
                    gamma_g_corr = gamma_g_sep * g;
            }
            return gamma_g_corr;
        }

        public static double Rs_DE_GHETTO_1994_HEAVY_OIL(Context ctx)
        {
            var gamma_o = ctx[Arg.GAMMA_O];
            if (!(U.isGE(gamma_o, 0.920) && gamma_o < 1.0))
            {
                //printf("Rs_DE_GHETTO_1994_HEAVY_OIL: gamma_o not in [0.920, 1.0): %.12g\n",
                //    gamma_o);
            }

            var P = ctx[Prm.P];
            var Pb = ctx[Prm.Pb];

            double Pb_ = P < Pb ? P : Pb;

            double X = 10.9267 * ctx.APIcalc() / (1.8 * ctx[Prm.T]);
            double Y = Math.Pow(10, X);
            double b = U.Pow(145.0377 * Pb_, 1.2057);

            var a = ctx[Arg.GAMMA_G_CORR] / 313.348;// FIXME: what this?? * 0.887051;

            double rs = a * b * Y;
            return rs;
        }

        static double calcX(double Pb, double T, double x, double X3, double X4)
        {
            var c = U.Pow(U.Kelv2Fahr(T), X3);
            var d = U.Pow(145.0377 * Pb, X4);
            var X = x * c * d;
            return X;
        }

        public static double Rs_VELARDE_1996(Context ctx)
        {
            var P = ctx[Prm.P];
            var Pb = ctx[Prm.Pb];
            var Rsb = ctx[Arg.Rsb];

            if (U.isGE(P, Pb))
                return Rsb;

            double pr = (P - 0.101) / Pb;
            if (U.isLE(pr, 0.0))
                return 0.0;
            else if (U.isGE(pr, 1.0))
                return Rsb;

            double ax, bx, cx;
            {
                const double A0 = 9.73 * 1.0e-7;
                const double A1 = 1.672608;
                const double A2 = 0.929870;
                const double B0 = 0.022339;
                const double B1 = -1.00475;
                const double B2 = 0.337711;
                const double C0 = 0.725167;
                const double C1 = -1.485480;
                const double C2 = -0.164741;

                var gamma_g = ctx[Arg.GAMMA_G];
                var _api = ctx.APIcalc();

                ax = A0 * U.Pow(gamma_g, A1) * U.Pow(_api, A2);
                bx = B0 * U.Pow(gamma_g, B1) * U.Pow(_api, B2);
                cx = C0 * U.Pow(gamma_g, C1) * U.Pow(_api, C2);
            }

            const double A3 = 0.247235;
            const double A4 = 1.056052;
            const double B3 = 0.132795;
            const double B4 = 0.302065;
            const double C3 = -0.091330;
            const double C4 = 0.047094;

            var T = ctx[Prm.T];

            double A = calcX(Pb, T, ax, A3, A4);
            double B = calcX(Pb, T, bx, B3, B4);
            double C = calcX(Pb, T, cx, C3, C4);

            double Rsr = A * U.Pow(pr, B) + (1 - A) * U.Pow(pr, C);
            double rs = Rsb * Rsr;
            if (rs < 0 && rs >= -0.01) // иногда небольшой минус при стандартных условиях, можно принять за 0.
                return 0;
            return rs;
        }
        #endregion

        #region OilCompressibility
        public static double co_VASQUEZ_BEGGS_1980(Context ctx)
        {
            var a = 27.759655 * ctx[Prm.Rs];
            var b = 17.2 * U.Kelv2Fahr(ctx[Prm.T]);
            var c = 1180.0 * ctx[Arg.GAMMA_G_CORR];
            var d = 12.61 * ctx.APIcalc();
            var X = -1433.0 + a + b - c + d;
            var P = U.Max(ctx[Prm.P], 0.1);
            var co = X / (1.0e5 * P);
            return co;
        }

        public static double co_AGIP(Context ctx)
        {
            double a = 23.0094 * ctx[Prm.Rs];
            double b = 22.12 * U.Kelv2Fahr(ctx[Prm.T]);
            double c = 1323.8 * ctx[Arg.GAMMA_G_CORR];
            double d = 10.5 * ctx.APIcalc();
            double X = -1682.8 + a + b - c + d;
            double co = X / (1.0e5 * ctx[Prm.P]);
            return co;
        }
        #endregion

        #region Bo
        public static double Bo_DEFAULT(Context ctx)
        {
            var Bob = ctx[Prm.Bob]; // pvt.Bob.calc(pvt, P, T, Pb, Rsb, Rs);
            var dP = ctx[Prm.P] - ctx[Prm.Pb];
            if (dP <= 0)
                return Bob;
            var co = ctx[Prm.Co];// pvt.co.calc(pvt, P, T, Pb, Rsb, Rs);
            var B = Bob * Math.Exp(-co * dP);
            return B;
        }
        #endregion

        #region OilVolumeFactor
        public static double Bob_STANDING_1947(Context ctx)
        {
            var a = 5.615 * ctx[Prm.Rs];
            var c = 2.25 * ctx[Prm.T];
            var b = U.Pow(ctx[Arg.GAMMA_G] / ctx[Arg.GAMMA_O], 0.5);
            var e = 1.47 * 1.0e-4;
            var X = a * b + c - 575.0;
            var Bob = 0.972 + e * U.Pow(X, 1.175);
            return Bob;
        }

        public static double Bob_VASQUEZ_BEGGS_1980(Context ctx)
        {
            double C1, C2, C3;
            if (U.isGE(ctx[Arg.GAMMA_O], 0.876))
            {
                C1 = 2.597 * 1.0e-3;
                C2 = 1.751 * 1.0e-5;
                C3 = -1.005 * 1.0e-7;
            }
            else
            {
                C1 = 2.593 * 1.0e-3;
                C2 = 1.100 * 1.0e-5;
                C3 = 7.423 * 1.0e-9;
            }

            var x = ctx.APIcalc() / ctx[Arg.GAMMA_G_CORR];
            var X = x * (1.8 * ctx[Prm.T] - 520);
            var rs = ctx[Prm.Rs];
            var Bob = 1 + C1 * rs + X * (C2 + C3 * rs);
            return Bob;
        }
        #endregion

        #region OilDensity
        public static double Rho_o_MAT_BALANS(Context ctx)
        {
            var P = ctx[Prm.P];
            var Pb = ctx[Prm.Pb];
            var gamma_o = ctx[Arg.GAMMA_O];
            var gamma_g = ctx[Arg.GAMMA_G];
            var Rsb = ctx[Arg.Rsb];

            double rho_o;
            if (P > Pb)
            {
                //double bob = pvt.Bob.calc(pvt, P, T, Pb, Rsb, Rsb);
                var tmp = ctx.NewCtx().With(Prm.P, P).With(Prm.T, ctx[Prm.T]).With(Prm.Rs, Rsb).Done();
                var bob = tmp[Prm.Bob];
                //double rho_ob = calc1(pvt, Pb, T, Pb, Rsb, Rsb, bob);
                var rho_ob = (1000.0 * gamma_o + 1.206 * gamma_g * Rsb) / bob;
                // double co = pvt.co.calc(pvt, P, T, Pb, Rsb, Rs);
                var co = ctx[Prm.Co];
                rho_o = rho_ob * Math.Exp(co * (P - Pb));
            }
            else
                //return calc1(pvt, P, T, Pb, Rsb, Rs, bo);
                rho_o = (1000.0 * gamma_o + 1.206 * gamma_g * ctx[Prm.Rs]) / ctx[Prm.Bo];
            return rho_o;
        }
        #endregion

        #region Mu_od
        public static double Mu_od_BEAL_1946(Context ctx)
        {
            var _api = ctx.APIcalc();
            var k = 0.43 + 8.33 / _api;
            var X = Math.Pow(10, k);
            var a = (0.32 + 1.8 * 1.0e7 / U.Pow(_api, 4.53));
            var b = 360.0 / (1.8 * ctx[Prm.T] - 260);
            var mu_od = a * U.Pow(b, X);
            return mu_od;
        }

        public static double Mu_od_AGIP(Context ctx)
        {
            var a = 0.025548 * ctx.APIcalc();
            var b = 0.56238 * U.log10(U.Kelv2Fahr(ctx[Prm.T]));
            var l = 1.8513 - a - b;
            var mu_od = Math.Pow(10, Math.Pow(10, l)) - 1.0;
            return mu_od;
        }
        #endregion

        #region Mu_os
        public static double Mu_os_BEGGS_ROBINSON_1975(Context ctx)
        {
            var a = ctx[Prm.Rs] / 0.1801;
            var A = 10.715 * U.Pow(a + 100, -0.515);
            var B = 5.44 * U.Pow(a + 150, -0.338);
            var mu_os = A * U.Pow(ctx[Prm.Mu_od], B);
            return mu_os;
        }

        public static double Mu_os_AGIP(Context ctx)
        {
            var rs = ctx[Prm.Rs];
            var y = Math.Pow(10, -0.002199 * rs);
            var a = 0.7024 * Math.Pow(10, -0.00324 * rs);
            var b = U.Pow(ctx[Prm.Mu_od], 0.172 + 0.7881 * y);
            var X = (0.1615 + a) * b;
            var mu_os = -0.032124 + 0.9289 * X - 0.02865 * X * X;
            return mu_os;
        }

        #endregion

        #region Mu_o
        public static double Mu_o_BEAL_1946(Context ctx)
        {
            var a = 0.14504 * (ctx[Prm.P] - ctx[Prm.Pb]);
            var mu_os = ctx[Prm.Mu_os];
            var b = 0.024 * U.Pow(mu_os, 1.6);
            var c = 0.038 * U.Pow(mu_os, 0.56);
            var mu_o = mu_os + a * (b + c);
            return Math.Max(mu_o, 0.0);
        }

        public static double Mu_o_VASQUEZ_BEGGS_1980(Context ctx)
        {
            var P = ctx[Prm.P];
            var a = U.Pow(145.0377 * P, 1.187);
            var b = -11.513 - 1.3024 * 1.0e-2 * P;
            var X = 2.6 * a * Math.Exp(b);
            var mu_o = ctx[Prm.Mu_os] * U.Pow(P / ctx[Prm.Pb], X);
            return U.Max(mu_o, 0.0);
        }
        #endregion

        #region OilSpecificHeat
        public static double Cpo_MUSTAFAEV_1974(Context ctx)
        {
            var cpo = 3390.0 - 1.6747 * ctx[Prm.Rho_o];
            return cpo;
        }

        public static double Cpo_GIMATUDINOV_1983(Context ctx)
        {
            var a = (ctx[Prm.T] - 223.15) / 100.0;
            var b = (1.14 - 1000.0 * ctx[Arg.GAMMA_O]);
            var cpo = 1507.2 * (1 + a * b);
            return cpo;
        }

        public static double Cpo_SHYLOV_1985(Context ctx)
        {
            var gamma_o = ctx[Arg.GAMMA_O];
            var gamma_g = ctx[Arg.GAMMA_G];
            var Rs = ctx[Prm.Rs];

            double Mo;// = calcMo(rs);
            {
                var MoD = 40.148 * gamma_o / (1.0 - 0.075 * gamma_o);
                var a = 1000.0 * gamma_o;
                var b = 1.206 * gamma_g;
                var c = a / MoD + Rs / 24.055;
                Mo = (a + b * Rs) / c;
            }

            var T = ctx[Prm.T];
            //double Tz = 343.0; // TODO: remove hardcoded Tz

            double T0, A0, A1, A2, A3, B0, B1, B2, B3, C0, C1;
            if (T < 293.0) // Tz -> T
            {
                T0 = 293.0;
                A0 = 2.3504;
                A1 = 4.6588;
                A2 = -1.3406;
                A3 = -5.344;
                B0 = 15.73;
                B1 = -0.6114 * 1.0e-4;
                B2 = 8.645;
                B3 = -114.24;
                C0 = 1.218;
                C1 = 0.358;
            }
            else
            {
                T0 = 343.0;
                A0 = 2.897;
                A1 = 0.2991;
                A2 = 0.1611;
                A3 = -1.7524;
                B0 = 7.8392;
                B1 = -0.5233 * 1.0e-3;
                B2 = 5.0285;
                B3 = -54.32;
                C0 = 1.232;
                C1 = 0.332;
            }

            double alpha_t; // = calcAlphaT(Mo, B0, B1, B2, B3, C0, C1);
            {
                var ndT0 = C0 + C1 * gamma_o * gamma_o;
                var a = B1 * Mo;
                var b = B2 * gamma_o * gamma_o * gamma_o;
                var c = B3 * U.log10(ndT0);
                alpha_t = 1.0e3 * (B0 + a + b + c);
            }

            double Cpt0;// = calcCpT0(Mo, A0, A1, A2, A3);
            {
                var a = U.Pow(U.log10(Mo), 1.0 / 3.0);
                var b = A1 * a;
                var c = A2 * a / gamma_o;
                var d = A3 / gamma_o;
                Cpt0 = 1.0e3 * (A0 + b + c + d);
            }

            double Cpo = Cpt0 * (1 + alpha_t * (T - T0));
            return Cpo;
        }

        #endregion

        #region OilGasSurfaceTension
        public static double Sigma_og_BAKER_SWERDLOFF_1956(Context ctx)
        {
            var T = ctx[Prm.T];
            var _api = ctx.APIcalc();

            // calcSigmaODG_293
            var s293 = 1.0e-3 * (39.0 - 0.2571 * _api);
            // calcSigmaODG_311
            var s311 = 1.0e-3 * (37.5 - 0.2571 * _api);

            double sigma_odg;
            if (U.isLE(T, 293.2))
                sigma_odg = s293;
            else if (U.isGE(T, 310.9))
                sigma_odg = s311;
            else
                sigma_odg = s293 - (T - 293.2) * (s293 - s311) / 17.8;

            var sigma_og = sigma_odg * Math.Exp(-8.6306 * 1.0e-4 * 145.0377 * ctx[Prm.P]);
            return sigma_og;
        }

        public static double Sigma_og_GIMATUDINOV_1983(Context ctx)
        {
            var P = ctx[Prm.P];
            var T = ctx[Prm.T];
            var a = -(1.58 + 0.05 * P);
            var sigma_og = Math.Pow(10, a) - 72 * 1.0e-6 * (T - 305.0);
            return sigma_og;
        }
        #endregion

        #region OilWaterSurfaceTension
        public static double Sigma_ow_GIMATUDINOV_1983(Context ctx)
        {
            var P = ctx[Prm.P];
            var T = ctx[Prm.T];
            var a = -(1.58 + 0.05 * P);
            var b = -(1.19 + 0.01 * P);
            var sigma_og = Math.Pow(10, a) - 72 * 1.0e-6 * (T - 305.0);
            var sigma_wg = Math.Pow(10, b);
            var sigma_ow = sigma_wg - sigma_og;
            return sigma_ow;
        }
        #endregion

        #region Tpc
        public static double Tpc_LONDONO_2002(Context ctx)
        {
            var gamma_g_ = ctx[Arg.GAMMA_G];
            var a = 305.26 * gamma_g_;
            var b = 52.23 * gamma_g_ * gamma_g_;
            return 22.44 + a - b;
        }

        public static double Tpc_SUTTON_2005(Context ctx)
        {
            var gamma_g_ = ctx[Arg.GAMMA_G];
            var a = 238.33 * gamma_g_;
            var b = 34.94 * gamma_g_ * gamma_g_;
            return 66.72 + a - b;
        }
        #endregion

        #region Ppc
        public static double Ppc_LONDONO_2002(Context ctx)
        {
            var gamma_g_ = ctx[Arg.GAMMA_G];
            var a = 0.4845 * gamma_g_;
            var b = 0.0624 * gamma_g_ * gamma_g_;
            return 5.0048 - a - b;
        }

        public static double Ppc_SUTTON_2005(Context ctx)
        {
            var gamma_g_ = ctx[Arg.GAMMA_G];
            var a = 0.8646 * gamma_g_;
            var b = 0.0407 * gamma_g_ * gamma_g_;
            return 5.1297 - a - b;
        }
        #endregion

        #region RealGasFactor
        public static double Z_BBS_1974(Context ctx)
        {
            var Tr = ctx[Prm.T] / ctx[Prm.Tpc];
            var Pr = ctx[Prm.P] / ctx[Prm.Ppc];
            var A1 = 1.39 * U.Pow(Tr - 0.92, 0.5) - 0.36 * Tr - 0.101;
            var a = Pr * (0.62 - 0.23 * Tr);
            var b = Pr * Pr * (0.066 / (Tr - 0.86) - 0.037);
            var c = U.Pow(Pr, 6.0) * 0.32 * Math.Pow(10, -9.0 * (Tr - 1.0));
            var A2 = a + b + c;
            var A3 = 0.132 - 0.32 * U.log10(Tr);
            var A4 = Math.Pow(10, 0.3106 - 0.49 * Tr + 0.1824 * Tr * Tr);
            var Z = A1 + (1.0 - A1) * Math.Exp(-A2) + A3 * U.Pow(Pr, A4);
            return Z;
        }

        public static double Z_DAK_1975(Context ctx)
        {
            var Tr = ctx[Prm.T] / ctx[Prm.Tpc];
            var Pr = ctx[Prm.P] / ctx[Prm.Ppc];
            var Z_low = 0.1;
            var Z_high = 5.0;
            var Z_mid = 0.0;

            for (int i = 0; i < 20; ++i)
            {
                Z_mid = 0.5 * (Z_high + Z_low);

                double Z;
                {
                    const double A1 = 0.3265;
                    const double A2 = -1.0700;
                    const double A3 = -0.5339;
                    const double A4 = 0.01569;
                    const double A5 = -0.05165;
                    const double A6 = 0.5475;
                    const double A7 = -0.7361;
                    const double A8 = 0.1844;
                    const double A9 = 0.1056;
                    const double A10 = 0.6134;
                    const double A11 = 0.7210;
                    var Rho_r = 0.27 * Pr / (Z_mid * Tr);
                    var Rho_r_2 = Rho_r * Rho_r;
                    var Rho_r_5 = Rho_r_2 * Rho_r_2 * Rho_r;
                    var Tr_ = Tr;
                    var Tr_2 = Tr_ * Tr_;
                    var Tr_3 = Tr_2 * Tr_;
                    var Tr_4 = Tr_2 * Tr_2;
                    var Tr_5 = Tr_4 * Tr_;
                    var a = A1 + A2 / Tr_ + A3 / Tr_3 + A4 / Tr_4 + A5 / Tr_5;
                    var b = A6 + A7 / Tr_ + A8 / Tr_2;
                    var c = A9 * (A7 / Tr_ + A8 / Tr_2);
                    var d = A10 * (1.0 + A11 * Rho_r_2) * Rho_r_2 / Tr_3;
                    var e = Math.Exp(-A11 * Rho_r_2);
                    Z = 1.0 + a * Rho_r + b * Rho_r_2 - c * Rho_r_5 + d * e;
                }

                if (Z > Z_mid)
                    Z_low = Z_mid;
                else
                    Z_high = Z_mid;

                if (Math.Abs(Z_low - Z_high) < 0.001)
                    break;
            }
            return Z_mid;
        }

        public static double Z_PAPAY_1985(Context ctx)
        {
            var Tr = ctx[Prm.T] / ctx[Prm.Tpc];
            var Pr = ctx[Prm.P] / ctx[Prm.Ppc];
            var a = 3.52 * Pr / Math.Pow(10, 0.9813 * Tr);
            var b = 0.274 * Pr * Pr / Math.Pow(10, 0.8157 * Tr);
            var Z = 1.0 - a + b;
            return Z;
        }
        #endregion

        #region GasCompressibility
        public static double cg_DEFAULT(Context ctx)
        {
            var Tr = ctx[Prm.T] / ctx[Prm.Tpc];
            var Ppc = ctx[Prm.Ppc];
            var Pr = ctx[Prm.P] / Ppc;

            double Z = Z_DAK_1975(ctx);

            const double A1 = 0.3265;
            const double A2 = -1.0700;
            const double A3 = -0.5339;
            const double A4 = 0.01569;
            const double A5 = -0.05165;
            const double A6 = 0.5475;
            const double A7 = -0.7361;
            const double A8 = 0.1844;
            const double A9 = 0.1056;
            const double A10 = 0.6134;
            const double A11 = 0.7210;

            var Rho_r = 0.27 * Pr / (Z * Tr);
            var Rho_r_2 = Rho_r * Rho_r;
            var Rho_r_4 = Rho_r_2 * Rho_r_2;
            var Tr_ = Tr;
            var Tr_2 = Tr_ * Tr_;
            var Tr_3 = Tr_2 * Tr_;
            var Tr_4 = Tr_2 * Tr_2;
            var Tr_5 = Tr_4 * Tr_;
            var a = A1 + A2 / Tr_ + A3 / Tr_3 + A4 / Tr_4 + A5 / Tr_5;
            var b = 2.0 * (A6 + A7 / Tr_ + A8 / Tr_2);
            var c = 5.0 * A9 * (A7 / Tr_ + A8 / Tr_2);
            var d = 1 + A11 * Rho_r_2 - A11 * A11 * Rho_r_4;
            var e = Math.Exp(-A11 * Rho_r_2);
            var f = 2 * A10 * Rho_r / Tr_3 * d * e;
            var dZ = a + b * Rho_r - c * Rho_r_4 + f;
            var k = 1.0 + Rho_r / Z * dZ;
            var h = 0.27 / (Z * Z * Tr);
            var p = 1.0 / Pr;
            var cgr = p - h * dZ / k;
            var cg = cgr / Ppc;
            return cg;
        }
        #endregion

        #region GasVolumeFactor
        public static double Bg_MAT_BALANS(Context ctx)
        {
            var K = 3.4564 * 1.0e-4;
            var Bg = K * ctx[Prm.Z] * ctx[Prm.T] / ctx[Prm.P];
            return Bg;
        }
        #endregion

        #region GasDensity
        public static double Rho_g_DEFAULT(Context ctx)
        {
            const double R = 8.31;
            //var M_G = ctx[Arg.GAMMA_G] * AirMolarMass; // молярная масса газа, г/моль
            var M_G = 24;
            var rho_g = 1000.0 * ctx[Prm.P] * M_G / (ctx[Prm.Z] * R * ctx[Prm.T]);
            return rho_g;
        }
        #endregion

        #region GasViscosity
        public static double Mu_g_LGE_MCCAIN_1991(Context ctx)
        {
            var T = ctx[Prm.T];
            var rho_g = ctx[Prm.Rho_g]; // pvt.Rho_g.calc(pvt, P, T);
            var Mg_ = 28.966 * ctx[Arg.GAMMA_G];
            var a = (9.379 + 0.01607 * Mg_) * U.Pow(1.8 * T, 1.5);
            var b = 209.2 + 19.26 * Mg_ + 1.8 * T;
            var X1 = a / b;
            var X2 = 3.448 + 986.4 / (1.8 * T) + 0.01009 * Mg_;
            var X3 = 2.447 - 0.2224 * X2;
            var mu_g = 1.0e-4 * X1 * Math.Exp(X2 * U.Pow(rho_g * 1.0e-3, X3));
            return U.Max(mu_g, 0.0);
        }

        public static double Mu_g_LONDONO_2002(Context ctx)
        {
            var P = ctx[Prm.P];
            var T = ctx[Prm.T];

            double mu_g_1atm; // = calcMuG1atm(T);
            {
                var log_gamma_g = Math.Log(ctx[Arg.GAMMA_G]);
                var log_T = Math.Log(1.8 * T);
                var a = -0.6045922 * log_gamma_g;
                var b = 0.749768 * log_T;
                var c = 0.1261051 * log_gamma_g * log_T;
                var d = 6.9718 * 1.0e-2 * log_gamma_g;
                var e = -0.1013889 * log_T;
                var f = -2.15294 * 1.0e-2 * log_gamma_g * log_T;
                var log_mu_g = (-6.39821 + a + b + c) / (1.0 + d + e + f);
                mu_g_1atm = Math.Exp(log_mu_g);
            }
            double F;// = calcF(pvt, P, T);
            {
                var rho_g = ctx[Prm.Rho_g];
                var rho_g_ = 1.0e-3 * rho_g;
                var rho_g_2 = rho_g_ * rho_g_;
                var rho_g_3 = rho_g_2 * rho_g_;
                var t = U.Kelv2Fahr(T);
                var tt = t * t;
                var A0 = 0.953363 - 1.007384 * t + 1.31729e-3 * tt;
                var A1 = -0.971028 + 11.2077 * t + 9.013e-2 * tt;
                var A2 = 1.01803 + 4.98986 * t + 0.302737 * tt;
                var A3 = -0.990531 + 4.17585 * t - 0.63662 * tt;
                var B0 = 1.0 - 3.19646 * t + 3.90961 * tt;
                var B1 = -1.00364 - 0.181633 * t - 7.79089 * tt;
                var B2 = 0.99808 - 1.62108 * t + 6.34836e-4 * tt;
                var B3 = -1.00103 + 0.676875 * t + 4.62481 * tt;
                var a = A0 + A1 * rho_g_ + A2 * rho_g_2 + A3 * rho_g_3;
                var b = B0 + B1 * rho_g_ + B2 * rho_g_2 + B3 * rho_g_3;
                F = a / b;
            }
            return U.Max(mu_g_1atm + F, 0.0);
        }
        #endregion

        #region GasSpecificHeat
        public static double Cg_SHYLOV_1985(Context ctx)
        {
            var T = ctx[Prm.T];
            var Tr = T / ctx[Prm.Tpc];
            var Pr = ctx[Prm.P] / ctx[Prm.Ppc];
            var Mg_ = 28.966 * ctx[Arg.GAMMA_G];
            var X1 = T * T;
            var X2 = Mg_;
            var Y1 = Math.Sqrt(Pr / (Tr * Tr));
            var Y2 = Pr * Math.Sqrt(Pr / U.Pow(Tr, 9));
            var A0 = 0.5374;
            var A1 = 0.117 * 1.0e-5;
            var A2 = -0.7 * 1.0e-2;
            var B0 = -0.7491;
            var B1 = 4.599;
            var B2 = 13.56;
            var A = A0 + A1 * X1 + A2 * X2;
            var B = B0 + B1 * Y1 + B2 * Y2;
            var Cg = 4.1876 * 1.0e3 * (A + B * 1.0 / Mg_);
            return Cg;
        }
        #endregion

        #region GasThermalConductivity 
        public static double Lambda_g_SHYLOV_1985(Context ctx)
        {
            var T = ctx[Prm.T];
            var Tr = T / ctx[Prm.Tpc];
            var Pr = ctx[Prm.P] / ctx[Prm.Ppc];
            var Mg_ = 28.966 * ctx[Arg.GAMMA_G];
            var X1 = T * T;
            var X2 = U.Pow(Mg_, 4);
            var X3 = Mg_;
            var Y1 = Pr / (Tr * Tr);
            var Y2 = U.Pow(Pr, 3) / U.Pow(Tr, 7);
            var Y3 = Pr * Pr;
            const double A0 = 29.74;
            const double A1 = 0.193 * 1.0e-3;
            const double A2 = 0.5 * 1.0e-6;
            const double A3 = -1.061;
            const double B0 = 0.9965;
            const double B1 = 0.3692;
            const double B2 = 0.1337;
            const double B3 = 0.6142 * 1.0e-2;
            var A = A0 + A1 * X1 + A2 * X2 + A3 * X3;
            var B = B0 + B1 * Y1 + B2 * Y2 + B3 * Y3;
            var lambda_g = 1.163 * 1.0e-3 * A * B;
            return lambda_g;
        }
        #endregion

        static double _salinityProc(Context ctx) => ctx[Arg.S] / (10000.0 * ctx[Arg.GAMMA_W]);

        public static double WaterSalinity_From_Density(double dens)
        {
            const double dA = 0.998234; // salinity = 0
            const double dB = 1.075478; // salinity = 100000
            const double Coeff = 100000 / (dB - dA);
            var sal = (dens - dA) * Coeff;
            return sal >= 0 ? sal : 0;
        }
        #region Rsw

        public static double Rswb_GIMATUDINOV_1983(Context ctx)
        {
            var P_bp_w = ctx[Arg.Pb_w];
            var P = ctx[Prm.P];
            var Pb_ = (P < P_bp_w) ? P : P_bp_w;
            const double alpha_g = 0.15;
            return alpha_g * Pb_;
        }

        public static double Rswb_SHYLOV_1985(Context ctx)
        {
            var P_bp_w = ctx[Arg.Pb_w];
            var P = ctx[Prm.P];
            var T = ctx[Prm.T];
            var Pb_ = (P < P_bp_w) ? P : P_bp_w;
            double logT_ = Math.Log(T - 273.15);
            double A = 0.3705 * Math.Exp(0.114 * logT_);
            double B = 0.1477 - 0.553 * logT_;
            double a = 0.139 - 0.22 * logT_;
            double b = Math.Exp(A * (Math.Log(Pb_) + 2.3214) + B);
            double SalinityProc = _salinityProc(ctx);
            double Rsw = (1.0 - a * SalinityProc) * b;
            return Rsw;
        }
        #endregion

        #region WaterCompressibility 
        public static double cw_SHYLOV_1985(Context ctx)
        {
            var P = ctx[Prm.P];
            double Rsw;
            if (P < 0.1)
                Rsw = ctx.NewWith(Prm.P, 0.1)[Prm.Rsw];
            else
                Rsw = ctx[Prm.Rsw];
            var cw = ctx[Arg.CWD] * (1.0 + 0.014 * Rsw);
            return cw;
        }

        public static double cw_OSIF_1988(Context ctx)
        {
            var P_ = U.Max(ctx[Prm.P], 0.1);
            var a = 7.033 * P_;
            var b = 3.7328 * 1.0e-3 * ctx[Arg.S];
            var c = 3.702486 * (1.8 * ctx[Prm.T] - 459.67);
            var r = a + b - c + 2780.656;
            if (U.isZero(r))
                return 0.0;
            var cw = 1.0 / r;
            return cw;
        }

        public static double cw_MCCAIN_1990(Context ctx)
        {
            double Bw = ctx[Prm.Bw]; // pvt.Bw.calc(pvt, P, T, Rho_w_sc);
            double Bg = ctx[Prm.Bg]; // pvt.Bg.calc(pvt, P, T);

            const double B0 = 1.01021 * 1.0e-2;
            const double B1 = -7.44241 * 1.0e-5;
            const double B2 = 3.05553 * 1.0e-7;
            const double B3 = -2.94883 * 1.0e-10;
            const double C0 = 9.02505;
            const double C1 = -0.130237;
            const double C2 = 8.53425 * 1.0e-4;
            const double C3 = -2.34122 * 1.0e-6;
            const double C4 = 2.37049 * 1.0e-9;
            var T = ctx[Prm.T];
            var t = U.Kelv2Fahr(T);
            var t2 = t * t;
            var t3 = t2 * t;
            var t4 = t2 * t2;
            var B = B0 + B1 * t + B2 * t2 + B3 * t3;
            var C = -1.0e-7 * (C0 + C1 * t + C2 * t2 + C3 * t3 + C4 * t4);
            var P = ctx[Prm.P];
            var d = 25.81842 * B + 7489.02658 * C * P;
            var cwb = cw_OSIF_1988(ctx); //new cw_OSIF_1988(null, pvtParams).calc(pvt, P, T, Rho_w_sc);
            var cw = cwb + (Bg / Bw) * d;
            return cw;
        }
        #endregion

        #region WaterVolumeFactor
        public static double Bw_SHYLOV_1985(Context ctx)
        {
            var Rsw = ctx[Prm.Rsw]; //pvt.Rsw.calc(pvt, P, T, Rho_w_sc);
            var Rho_w = ctx[Prm.Rho_w]; //pvt.Rho_w.calc(pvt, P, T, Rho_w_sc);
            var a = ctx[Arg.Rho_w_sc] * (1.0 + 8 * 1.0e-4 * Rsw);
            var Bw = a / Rho_w;
            return Bw;
        }

        public static double Bw_MCCAIN_1990(Context ctx)
        {
            const double A1 = -1.95301 * 1.0e-9;
            const double A2 = -1.72834 * 1.0e-13;
            const double A3 = -3.58922 * 1.0e-7;
            const double A4 = -2.25341 * 1.0e-10;
            const double B0 = -1.0001 * 1.0e-2;
            const double B1 = 1.33391 * 1.0e-4;
            const double B2 = 5.50654 * 1.0e-7;
            var P_ = 145.03768 * ctx[Prm.P];
            var P_2 = P_ * P_;
            var T_ = 1.8 * ctx[Prm.T] - 459.67;
            var T_2 = T_ * T_;
            var dP = A1 * P_ * T_ + A2 * P_2 * T_ + A3 * P_ + A4 * P_2;
            var dT = B0 + B1 * T_ + B2 * T_2;
            var Bw = (1.0 + dP) * (1.0 + dT);
            return Bw;
        }
        #endregion

        #region WaterDensity
        public static double Rho_w_GIMATUDINOV_1983(Context ctx)
        {
            var T = ctx[Prm.T];
            var a = 1.0e-4 * (T - 273.0);
            var b = 0.269 * U.Pow(T - 273.0, 0.637) - 0.8;
            var r = 1.0 + a * b;
            var Rho_w = ctx[Arg.Rho_w_sc] / r;
            return Rho_w;
        }

        public static double Rho_w_SHYLOV_1985(Context ctx)
        {
            var Rsw = ctx[Prm.Rsw]; // pvt.Rsw.calc(pvt, P, T, Rho_w_sc);
            var cw = ctx[Prm.Cw]; // pvt.cw.calc(pvt, P, T, Rho_w_sc);
            var Rho_w_sc = ctx[Arg.Rho_w_sc];
            var P = ctx[Prm.P];
            var T = ctx[Prm.T];
            var a = Rho_w_sc * (1.0 - Rsw / Rho_w_sc);
            var b = (1.0 - cw * P);
            var c = 0.5 * (T - 273.15) + 10.0;
            var d = (T - 293.15) * 1.0e-5;
            var Rho_w = a / (b * (1 + c * d));
            return Rho_w;
        }

        public static double Rho_w_MCCAIN_1990(Context ctx)
        {
            const double A1 = -1.95301 * 1.0e-9;
            const double A2 = -1.72834 * 1.0e-13;
            const double A3 = -3.58922 * 1.0e-7;
            const double A4 = -2.25341 * 1.0e-10;
            const double B0 = -1.0001 * 1.0e-2;
            const double B1 = 1.33391 * 1.0e-4;
            const double B2 = 5.50654 * 1.0e-7;
            var P_ = 145.03768 * ctx[Prm.P];
            var P_2 = P_ * P_;
            var T_ = 1.8 * ctx[Prm.T] - 459.67;
            var T_2 = T_ * T_;
            var dP = A1 * P_ * T_ + A2 * P_2 * T_ + A3 * P_ + A4 * P_2;
            var dT = B0 + B1 * T_ + B2 * T_2;
            var Bw = (1.0 + dP) * (1.0 + dT);
            var SalinityProc = _salinityProc(ctx);
            var rho_w_sc = 999.039 + 7.02574 * SalinityProc + 0.256414 * SalinityProc * SalinityProc;
            var Rho_w = rho_w_sc / Bw;
            return Rho_w;
        }
        #endregion

        #region WaterViscosity
        public static double Mu_w_SHYLOV_1985(Context ctx)
        {
            double Mu_w = Math.Exp(1641.2 / ctx[Prm.T] - 5.5908);
            return U.Max(Mu_w, 0.0);
        }

        public static double Mu_w_MCCAIN_1990(Context ctx)
        {
            const double A0 = 0.9994;
            const double A1 = 4.0295 * 1.0e-5;
            const double A2 = 3.1062 * 1.0e-9;
            const double B0 = 109.574;
            const double B1 = -8.40564;
            const double B2 = 0.313314;
            const double B3 = 8.72213 * 1.0e-3;
            const double C0 = 1.12166;
            const double C1 = -2.63951 * 1.0e-2;
            const double C2 = 6.79461 * 1.0e-4;
            const double C3 = 5.74119 * 1.0e-5;
            const double C4 = -1.55586 * 1.0e-6;
            var SalinityProc = _salinityProc(ctx);
            var S_ = SalinityProc;
            var S_2 = SalinityProc * SalinityProc;
            var S_3 = S_ * S_2;
            var S_4 = S_2 * S_2;
            var B = B0 + B1 * S_ + B2 * S_2 + B3 * S_3;
            var C = C0 + C1 * S_ + C2 * S_2 + C3 * S_3 + C4 * S_4;
            var Mu_w_1atm = B * U.Pow(1.8 * ctx[Prm.T] - 459.67, -C);
            var P_ = 145.03678 * ctx[Prm.P];
            var P_2 = P_ * P_;
            var Mu_w = Mu_w_1atm * (A0 + A1 * P_ + A2 * P_2);
            return U.Max(Mu_w, 0.0);
        }
        #endregion

        #region WaterGasSurfaceTension
        public static double Sigma_wg_RAMEY_1973(Context ctx)
        {
            var Sigma_wg = 1.0e-3 * (20.0 + 36.0 * 1.0e-3 * (ctx[Prm.Rho_w] - ctx[Prm.Rho_g]));
            return Sigma_wg;
        }

        public static double Sigma_wg_GIMATUDINOV_1983(Context ctx)
        {
            double Sigma_wg = Math.Pow(10, -1.19 + 0.01 * ctx[Prm.P]);
            return Sigma_wg;
        }
        #endregion

        #region WaterThermalConductivity
        public static double Lambda_w_GIMATUDINOV_1983(Context ctx)
        {
            var T = ctx[Prm.T];
            var a = (T < 398) ? U.Pow(398 - T, 2.45) : 0;
            var Lambda_w = 0.686 - 1.0e-6 * a;
            return Lambda_w;
        }
        #endregion
    }
}
