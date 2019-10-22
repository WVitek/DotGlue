using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace W.Oilca
{
    public class InvalidValueError : Exception
    {
        private readonly string _msg;
        public InvalidValueError(string msg) { _msg = msg; }
        public override string ToString() => $"InvalidValueError: {_msg}";
    }

    public class CalcValidationException : Exception
    {
        public CalcValidationException(string message) : base(message) { }
        public CalcValidationException(string message, Exception inner) : base(message, inner) { }
        public CalcValidationException(string paramName, string message) : base($"{message}. Param: {paramName}") { }
    }

    public static class Gradient
    {
        public class DataInfo
        {
            /// <summary>
            /// плотность жидкости
            /// </summary>
            public double rho_l;
            /// <summary>
            /// плотность смеси
            /// </summary>
            public double rho_n;
            /// <summary>
            /// Liquid surface tension
            /// </summary>
            public double sigma_lg;
            /// <summary>
            /// Вязкость жидкости
            /// </summary>
            public double mu_l;
            /// <summary>
            /// Скорость жидкости
            /// </summary>
            public double Vsl;
            /// <summary>
            /// Скорость нефти
            /// </summary>
            public double Vso;
            /// <summary>
            /// Скорость воды
            /// </summary>
            public double Vsw;
            /// <summary>
            /// Скорость газа
            /// </summary>
            public double Vsg;
            /// <summary>
            /// Скорость смеси
            /// </summary>
            public double Vm;

            public double Q_gas_free_sc;
            public double gasVolumeFraction;

            /// <summary>
            /// Дебит нефти в условиях насоса
            /// </summary>
            public double Q_oil_rate;
            /// <summary>
            /// Дебит газа в условиях насоса
            /// </summary>
            public double Q_gas_rate;
            /// <summary>
            /// Дебит воды в условиях насоса
            /// </summary>
            public double Q_water_rate;
            /// <summary>
            /// Тип потока
            /// </summary>
            public int flowPattern;

            public DataInfo Clone() => (DataInfo)MemberwiseClone();
        }

        /// <param name="ctx">Для доступа к PVT-свойствам в условиях</param>
        /// <param name="D_mm">Диаметр, мм</param>
        /// <param name="theta">Угол наклона, если отрицательный - расчет вверх</param>
        /// <param name="eps">Шероховатость</param>
        /// <param name="q_osc">Дебит нефти</param>
        /// <param name="q_wsc">Дебит воды</param>
        /// <param name="q_gsc">Дебит газа</param>
        /// <param name="gradientData">Структура для записи выходных значений</param>
        /// <param name="Payne_et_all_holdup"></param>
        /// <param name="Payne_et_all_friction"></param>
        public delegate double Calc(
                PVT.Context ctx,
                double D_mm,
                double Theta,
                double Eps,
                double q_osc,
                double q_wsc,
                double q_gsc,
                DataInfo gradientData,
                bool Payne_et_all_holdup = false,
                bool Payne_et_all_friction = true
            );

        public static class BegsBrill
        {
            public static double Calc(
                PVT.Context ctx,
                double D_mm,
                double theta,
                double eps,
                double q_osc,
                double q_wsc,
                double q_gsc,
                DataInfo gd,
                bool Payne_et_all_holdup = false,
                bool Payne_et_all_friction = true)
            {
                // Calculate auxilary values
                // Pipe cross-sectional area
                double PI = 3.141592; // FIXME: World.PI
                double g = 9.8; // FIXME: World.g

                //double d = D_mm.get();

                double a_p = PI * D_mm * D_mm * 0.000001 / 4;
                var P_MPa = ctx[PVT.Prm.P];
                var T_K = ctx[PVT.Prm.T];
                //OilData oil = fluidSim.Oil().CalcData(P_MPa, T_K);
                //GasData gas = fluidSim.Gas().CalcData(P_MPa, T_K);
                //WaterData water = fluidSim.Water().CalcData(P_MPa, T_K);

                // Calculate flow rates at reference pressure
                double Q_o = 0.000011574 * q_osc * ctx[PVT.Prm.Bo]; // *oil.VolumeFactor;
                double Q_w = 0.000011574 * q_wsc * ctx[PVT.Prm.Bw]; // water.VolumeFactor;
                double Q_l = Q_o + Q_w;
                //double q_gas_free_sc = (q_gsc - oil.SolutionGOR * q_osc); // FIXME: revert back r_sw: - pvt.r_sw * q_wsc);
                double q_gas_free_sc = (q_gsc - ctx[PVT.Prm.Rs] * q_osc); // FIXME: revert back r_sw: - pvt.r_sw * q_wsc);
                                                                          //double Q_g = 0.000011574 * gas.VolumeFactor * q_gas_free_sc;
                double Q_g = 0.000011574 * ctx[PVT.Prm.Bg] * q_gas_free_sc;

                // if gas rate is negative - assign gas rate to zero
                if (Q_g < 0.0)
                    Q_g = 0.0;

                // calculate volume fraction of water in liquid at no-slip conditions
                double waterVolumeFraction = Q_w / Q_l;

                // volume fraction of liquid at no-slip conditions
                double liquidVolumeFraction = Q_l / (Q_l + Q_g);
                double gasVolumeFraction = Q_g / (Q_l + Q_g);

                // densities
                double rho_o = ctx[PVT.Prm.Rho_o]; // oil.Density;
                double rho_w = ctx[PVT.Prm.Rho_w]; // water.Density;
                double rho_g = ctx[PVT.Prm.Rho_g]; // gas.Density;
                double rho_l = ctx.Liq_Density(waterVolumeFraction);// fluidSim.Liquid().CalcDensity(waterVolumeFraction, P_MPa, T_K);

                // no-slip mixture density
                // fluidSim.Mixture().CalcDensity(waterVolumeFraction, liquidVolumeFraction, gasVolumeFraction, P_MPa, T_K, 0.0);
                double rho_n = ctx.Mix_Density(waterVolumeFraction, liquidVolumeFraction, gasVolumeFraction);

                // Liquid surface tension
                double sigma_l = ctx.Liq_LiquidGasSurfaceTension(waterVolumeFraction); // fluidSim.Liquid().CalcLiquidGasSurfaceTension(waterVolumeFraction, P_MPa, T_K);

                // Liquid viscosity
                double mu_l = ctx.Liq_Viscosity(waterVolumeFraction); // fluidSim.Liquid().CalcViscosity(waterVolumeFraction, P_Atm, T_C);

                // No slip mixture viscosity
                double mu_n = ctx.Mix_Viscosity(waterVolumeFraction, liquidVolumeFraction, gasVolumeFraction); // fluidSim.Mixture().CalcViscosity(waterVolumeFraction, liquidVolumeFraction, gasVolumeFraction, P_MPa, T_K);

                // Sureficial velocities
                double V_sl = Q_l / a_p;
                double V_sg = Q_g / a_p;
                double V_m = V_sl + V_sg;

                // Reinolds number
                double n_re = 1 * rho_n * V_m * D_mm / mu_n;

                // Froude number
                double n_fr = V_m * V_m / (g * D_mm * 0.001);

                // Liquid velocity number
                double N_lv = V_sl * Math.Pow(rho_l / (g * sigma_l), 0.25);

                // Pipe relative roughness
                double E = eps / (D_mm * 0.001);

                // -----------------------------------------------------------------------
                // determine flow pattern

                // flow_regime_cell.Value = flow_pattern
                // -----------------------------------------------------------------------
                // determine liquid holdup

                int flow_pattern = determineFlowPattern(n_fr, liquidVolumeFraction);
                double h_l = 0.0;
                if (flow_pattern == 0 || flow_pattern == 1 || flow_pattern == 2)
                {
                    h_l = h_l_theta(flow_pattern, liquidVolumeFraction, n_fr,
                        N_lv, theta, Payne_et_all_holdup);
                }
                else
                {
                    double l_2 = 0.000925 * Math.Pow(liquidVolumeFraction, -2.468);
                    double l_3 = 0.1 * Math.Pow(liquidVolumeFraction, -1.452);
                    double aa = (l_3 - n_fr) / (l_3 - l_2);
                    h_l = aa * h_l_theta(0, liquidVolumeFraction, n_fr, N_lv, theta, Payne_et_all_holdup) +
                        (1 - aa) * h_l_theta(1, liquidVolumeFraction, n_fr, N_lv, theta, Payne_et_all_holdup);
                }

                // Calculate normalized friction factor
                double f_n = calc_friction_factor(n_re, E,
                    Payne_et_all_friction);

                // calculate friction factor correction for multiphase flow
                double Y = U.Max(liquidVolumeFraction / (h_l * h_l), 0.001);
                double S = 0.0;

                if (Y > 1 && Y < 1.2)
                {
                    S = Math.Log(2.2 * Y - 1.2);
                }
                else
                {
                    double ly = Math.Log(Y);
                    double ly2 = ly * ly;
                    S = ly / (-0.0523 + 3.182 * ly - 0.8725 * (ly2) + 0.01853 * (ly2 * ly2));
                }

                //
                // calculate friction factor
                double f = f_n * Math.Exp(S);

                // calculate mixture density

                double rho_s = rho_l * h_l + rho_g * (1 - h_l);

                // calculate pressure gradient due to gravity
                double dpdl_g = rho_s * g * Math.Sin(PI / 180 * theta);
                // calculate pressure gradient due to friction
                double dpdl_f = f * rho_n * V_m * V_m / (2 * D_mm * 0.001);

                var P_Atm = ctx[PVT.Prm.P];
                double Ek = V_m * V_sg * rho_n / (1.0e6 * P_Atm);
                //printf("Ek = %#05.16g\n", Ek);

                if (gd != null)
                {
                    gd.rho_l = rho_l;
                    gd.rho_n = rho_n;
                    gd.sigma_lg = sigma_l;
                    gd.Vsl = V_sl;
                    gd.Vso = Q_o / a_p;
                    gd.Vsw = Q_w / a_p;
                    gd.Vsg = V_sg;
                    gd.mu_l = mu_l;
                    gd.Vm = V_m;
                    gd.Q_gas_free_sc = q_gas_free_sc;
                    gd.Q_oil_rate = Q_o * 86400;
                    gd.Q_gas_rate = Q_g * 86400;
                    gd.Q_water_rate = Q_w * 86400;
                    gd.gasVolumeFraction = gasVolumeFraction;
                    gd.flowPattern = flow_pattern;
                }

                // calculate pressure gradient
                return (0.000001 * (dpdl_g + dpdl_f));//.MPa2Atm();
            }

            public static int determineFlowPattern(double n_fr, double lambda_l)
            {
                double L1 = 316.0 * Math.Pow(lambda_l, 0.302);
                double L2 = 0.000925 * Math.Pow(lambda_l, -2.468);
                double L3 = 0.10 * Math.Pow(lambda_l, -1.452);
                double L4 = 0.5 * Math.Pow(lambda_l, -6.738);

                int flow_pattern = 0;
                if (lambda_l < 0.001 && n_fr < L1)
                    flow_pattern = 0;
                else if (U.isGE(lambda_l, 0.01) && n_fr < L2)
                    flow_pattern = 0;
                else if (U.isGE(lambda_l, 0.01) && U.isGE(n_fr, L2) && n_fr < L3)
                    flow_pattern = 3;
                else if (U.isGE(lambda_l, 0.01) && lambda_l < 0.4 && n_fr > L3 && U.isGE(L1, n_fr))
                    flow_pattern = 1;
                else if (U.isGE(lambda_l, 0.4) && L3 < n_fr && U.isGE(L4, n_fr))
                    flow_pattern = 1;
                else if (lambda_l < 0.4 && U.isGE(n_fr, L1))
                    flow_pattern = 2;
                else if (U.isGE(lambda_l, 0.4) && n_fr > L4)
                    flow_pattern = 2;
                return flow_pattern;
            }

            static readonly double[] hl_a = new double[] { 0.98, 0.845, 1.065 };
            static readonly double[] hl_B = new double[] { 0.4846, 0.5351, 0.5824 };
            static readonly double[] hl_C = new double[] { 0.0868, 0.0173, 0.0609 };
            static readonly double[] hl_E = new double[] { 0.011, 2.96, 1 };
            static readonly double[] hl_f = new double[] { -3.768, 0.305, 0 };
            static readonly double[] hl_g = new double[] { 3.539, -0.4473, 0 };
            static readonly double[] hl_h = new double[] { -1.614, 0.0978, 0 };

            static double h_l_theta(int flow_pattern, double lambda_l, double n_fr, double N_lv, double theta, bool Payne_et_all)
            {
                // function calculating liquid holdup
                // flow_pattern - flow pattern (0 -Segregated, 1 - Intermittent, 2 - Distributed)
                // lambda_l - volume fraction of liquid at no-slip conditions
                // n_fr - Froude number
                // n_lv - liquid velocity number
                // theta - pipe inclination angle, (Degrees)
                // payne_et_all - flag indicationg weather to applied Payne et all correction for holdup (0 - not applied, 1 - applied)

                if (flow_pattern > 2)
                    throw new InvalidValueError("h_l_theta should be called only for 0-2 flow patterns");

                double PI = 3.141592;

                // FIXME: check flow_pattern in [0, 3)
                // Constants to determine liquid holdup

                // calculate liquid holdup at no slip conditions
                double h_l_0 = hl_a[flow_pattern] * Math.Pow(lambda_l, hl_B[flow_pattern]) / Math.Pow(n_fr, hl_C[flow_pattern]);

                // calculate correction for inclination angle
                double cc = U.Max(0, (1 - lambda_l) * Math.Log(hl_E[flow_pattern] *
                      Math.Pow(lambda_l, hl_f[flow_pattern]) * Math.Pow(N_lv, hl_g[flow_pattern]) *
                      Math.Pow(n_fr, hl_h[flow_pattern])));

                // convert angle to radians
                double theta_d = PI / 180 * theta;

                // FIXME: Math.Sin
                double Psi = 1 + cc * (Math.Sin(1.8 * theta_d) + 0.333 *
                    Math.Pow(Math.Sin(1.8 * theta_d), 3));

                // calculate liquid holdup with payne et al. correction factor
                if (Payne_et_all)
                {
                    if (theta > 0.0)
                    {
                        // uphill flow
                        return U.Max(U.Min(1, 0.924 * h_l_0 * Psi), lambda_l);
                    }
                    else
                    {
                        // downhill flow
                        return U.Max(U.Min(1, 0.685 * h_l_0 * Psi), lambda_l);
                    }
                }
                else
                {
                    return U.Max(U.Min(1, h_l_0 * Psi), lambda_l);
                }
            }

            static double calc_friction_factor(double n_re, double E, bool Rough_pipe)
            {
                // Calculates friction factor given pipe relative roughness and Reinolds number
                // Parameters
                // n_re - Reinolds number
                // e - pipe relative roughness
                // Rough_pipe - flag indicating weather to calculate friction factor for rough pipe using Moody correlation (Rough_pipe > 0), or
                // using Drew correlation for smooth pipes

                // friction factor and iterated friction factor
                double f_n = 0.0;
                double f_n_new = 0.0;
                double f_int = 0.0;

                if (n_re > 2000.0)
                {
                    // turbulent flow
                    if (Rough_pipe)
                    {
                        // calculate friction factor for rough pipes according to Moody
                        // method - Payne et all modification for Beggs&Brill correlation

                        f_n = Math.Pow(2 * U.log10(2 / 3.7 * E - 5.02 / n_re *
                              U.log10(2 / 3.7 * E + 13 / n_re)), -2);

                        int i = 0;
                        //iterate until error in friction factor is sufficiently small
                        do
                        {
                            f_n_new = Math.Pow(1.74 - 2 * U.log10(2 * E +
                                  18.7 / (n_re * Math.Sqrt(f_n))), -2);
                            ++i;
                            f_int = f_n;
                            f_n = f_n_new;
                            // stop when error is sufficiently small or max number of
                            // iterations exceedied
                        }
                        while (!(Math.Abs(f_n_new - f_int) <= 0.001 || i > 19));
                    }
                    else
                    {
                        // Calculate friction factor for smooth pipes using Drew
                        // correlation - original Begs&Brill with no modification
                        f_n = 0.0056 + 0.5 * Math.Pow(n_re, -0.32);
                    }
                }
                else
                {
                    // laminar flow
                    f_n = 64 / n_re;
                }

                return f_n;
            }
        }
    }
}
