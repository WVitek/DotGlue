using System;
using System.Collections.Generic;
using System.Text;
using System.Linq.Expressions;
using System.Diagnostics;

namespace W.Oilca
{
    public static class PressureDrop
    {
        [DebuggerHidden]
        public static CalcValidationException InvalidValue<T>(string method, string param, T v)
        => throw new CalcValidationException(FormattableString.Invariant($"{nameof(PressureDrop)}.{method}.{param} has invalid value '{v}'"));

        public class StepInfo
        {
            public readonly Gradient.DataInfo gd;
            public readonly double WCT, GOR, L, dP;
            public StepInfo(Gradient.DataInfo gd, double WCT, double GOR, double L, double dP)
            {
                this.gd = gd;
                this.WCT = WCT;
                this.GOR = GOR;
                this.L = L;
                this.dP = dP;
            }
        }

        /// <summary>
        /// Расчёт температуры для дебита Qoil или Qliq(Qoil + Qwat) на расстоянии L от начала трубы
        /// </summary>
        /// <param name="Qoil"></param>
        /// <param name="Qwat"></param>
        /// <param name="H"></param>
        /// <returns>Температура, °K</returns>
        public delegate double CalcTemperatureK(double Qo_sc, double Qw_sc, double L_m);

        public delegate double GetZenithAngleAt(double L);

        public enum FlowDirection { Forward = +1, Backward = -1 };

        public static double dropLiq(
            PVT.Context.Leaf ctx,
            Gradient.DataInfo gd_out,
            double D_mm,
            double L0_m,
            double L1_m,
            double Roughness,
            FlowDirection flowDir,
            double P0_MPa,
            double LiquidSC_VOLRATE,
            double WCT,
            double GOR,
            double dL_m, // шаг по трубе для градиента, м
            double dP_MPa, // точность по давлению, атм
            double maxP_MPa, // макс. давление, атм
            List<StepInfo> stepsInfo,
            CalcTemperatureK getTempK,
            GetZenithAngleAt getAngle,
            Gradient.Calc gradCalc,
            bool WithFriction = true)
        {
            //if (stepsInfo == null)
            //    InvalidValue(nameof(dropLiq), nameof(stepsInfo), "null");
            if (getTempK == null)
                InvalidValue(nameof(dropLiq), nameof(getTempK), "null");
            if (gd_out == null)
                InvalidValue(nameof(dropLiq), nameof(gd_out), "null");

            //if (_ps.input == null)
            //    throw new CalcValidationException("PipePressure.ps.input is null");
            //if (_ps.calcParams == null)
            //    throw new CalcValidationException("PipePressure.ps.calcParams is null");
            //if (_ps.commonParams == null)
            //    throw new CalcValidationException("PipePressure.ps.commonParams is null");

            if (double.IsNaN(dL_m) || dL_m < 0)
                InvalidValue(nameof(dropLiq), nameof(dL_m), dL_m);

            if (double.IsNaN(dP_MPa) || dP_MPa < 0)
                InvalidValue(nameof(dropLiq), nameof(dP_MPa), dP_MPa);

            if (double.IsNaN(maxP_MPa) || maxP_MPa < 0)
                InvalidValue(nameof(dropLiq), nameof(maxP_MPa), maxP_MPa);

            if (double.IsNaN(D_mm) || D_mm < 0)
                InvalidValue(nameof(dropLiq), nameof(D_mm), D_mm);

            if (double.IsNaN(L0_m) || L0_m < 0)
                InvalidValue(nameof(dropLiq), nameof(L0_m), L0_m);

            if (double.IsNaN(L1_m) || L1_m < 0)
                InvalidValue(nameof(dropLiq), nameof(L1_m), L1_m);

            if (double.IsNaN(Roughness) || Roughness < 0)
                InvalidValue(nameof(dropLiq), nameof(Roughness), Roughness);

            if (double.IsNaN(P0_MPa) || P0_MPa < 0)
                InvalidValue(nameof(dropLiq), nameof(P0_MPa), P0_MPa);

            if (double.IsNaN(LiquidSC_VOLRATE) || LiquidSC_VOLRATE < 0)
                InvalidValue(nameof(dropLiq), nameof(LiquidSC_VOLRATE), LiquidSC_VOLRATE);

            if (double.IsNaN(GOR) || GOR < 0)
                InvalidValue(nameof(dropLiq), nameof(GOR), GOR);

            //stepsInfo.Clear();

            // calculate oil rate at standard conditions
            double q_osc = LiquidSC_VOLRATE * (1 - WCT);
            // calculate water rate at standard conditions
            double q_wsc = LiquidSC_VOLRATE * WCT;
            // calculate gas rate at standart conditions
            double q_gsc = U.Max(GOR, ctx[PVT.Arg.Rsb]) * q_osc;


            var P = P0_MPa;
            var L = L0_m;

            var length = Math.Abs(L0_m - L1_m);
            var dLsign = L0_m < L1_m ? 1.0 : -1.0;
            var dPsign = (flowDir == FlowDirection.Forward) ? -1 : +1;

            //var t_pvt = getTempK(q_osc, q_wsc, L);
            //if (t_pvt < 0.0)
            //{
            //    //ShowTemperatureWarning(t_pvt);
            //    t_pvt = 0;
            //}

            double delta_p = 0;

            var prevGradientData = (stepsInfo != null) ? gd_out.Clone() : null;
            //stepsInfo.Add(new StepInfo(ctx, prevGradientData, WCT, GOR, L, dP)); //, t_pvt.get(), delta_p, flowPattern: -1, gasVolumeFraction: 0.0));

            double delta_l = dL_m;
            int n_nodes = (int)(length / delta_l);
            double last_delta_l = 0;
            if (length > n_nodes * delta_l)
            {
                last_delta_l = length - n_nodes * delta_l;
                n_nodes += 1;
            }

            var gradCtx = ctx;
            var tmpCtx = ctx.NewCtx().Done();

            Func<PVT.Context.Leaf, double, double, Gradient.DataInfo, double> calcGradient = (calcCtx, l, p, data) =>
               {
                   var theta = getAngle(l);
                   var t = getTempK(q_osc, q_wsc, l);
                   if (t < 0.0)
                   {
                       //ShowTemperatureWarning(t);
                       t = 0;
                   }

                   calcCtx.Reuse().With(PVT.Prm.P, p).With(PVT.Prm.T, t).Done();

                   double g = gradCalc(calcCtx, D_mm, theta, Roughness,
                       q_osc, q_wsc, q_gsc, data,
                       false, WithFriction);
                   return g;
               };

            double processedLen = 0;
            int index = 0;
            var tmpGradientData = new Gradient.DataInfo();
            double tolerance = dP_MPa; // 1e-4 * _ps.calcParams.PRESSURE_DELTA.Value;
            var maxGradientPressure = maxP_MPa; // _ps.commonParams.MaxGradientPressure.get();

            const double minStep = 1;
            const double maxStep = 200;

            int nIterations = 0;

            for (; index <= n_nodes; ++index)
            {
                nIterations++;

                var K1 = calcGradient(gradCtx, L, P, gd_out);

                if (index == n_nodes)
                {
                    break;
                }

                if (last_delta_l > 0 && index == n_nodes - 1)
                    delta_l = last_delta_l;

                delta_p = K1 * dPsign * delta_l;

                Debug.Assert(!double.IsInfinity(K1) && !double.IsNaN(K1));
                Debug.Assert(!double.IsInfinity(delta_l) && !double.IsNaN(delta_l));
                Debug.Assert(!double.IsInfinity(delta_p) && !double.IsNaN(delta_p));

                var hasError = false;
                var P1 = P + delta_p;
                var P2 = 0.0;
                var P3 = 0.0;
                var P4 = 0.0;

                var K2 = 0.0;
                var K3 = 0.0;
                var K4 = 0.0;

                if (U.isLE(P1, 0.0))
                {
#if DEBUG
                    //_logger.Debug("P1: P1={P1}, P={P}, deltaP={dP}", P1, P, delta_p);
#endif
                    hasError = true;
                }

                if (!hasError)
                {
                    P2 = P + dPsign * K1 * delta_l / 2.0;
                    if (U.isLE(P2, 0.0))
                    {
#if DEBUG
                        //_logger.Debug("P2: P2={P2}, P={P}, deltaL={dL}, K1={K1}", P2, P, delta_l, K1);
#endif
                        hasError = true;
                    }
                }


                if (!hasError)
                {
                    K2 = calcGradient(tmpCtx, L + delta_l / 2.0, P2, tmpGradientData);
                    P3 = P + dPsign * K2 * delta_l / 2.0;
                    if (U.isLE(P3, 0.0))
                    {
#if DEBUG
                        //_logger.Debug("P3: P3={P3}, P={P}, deltaL={dL}, K2={K2}", P3, P, delta_l, K2);
#endif
                        hasError = true;
                    }
                }

                if (!hasError)
                {
                    K3 = calcGradient(tmpCtx, L + delta_l / 2.0, P3, tmpGradientData);
                    P4 = P + dPsign * K3 * delta_l;
                    if (U.isLE(P4, 0.0))
                    {
#if DEBUG
                        //_logger.Debug("P4: P4={P4}, P={P}, deltaL={dL}, K3={K3}", P4, P, delta_l, K3);
#endif
                        hasError = true;
                    }
                }

                if (!hasError)
                    K4 = calcGradient(tmpCtx, L + delta_l, P4, tmpGradientData);

                double err = 2.0 * tolerance;
                if (!hasError)
                    err = 2.0 / 3.0 * Math.Abs(K1 - K2 - K3 + K4);

                if (err < tolerance || U.isLE(delta_l, minStep))
                {
                    if (stepsInfo != null)
                    {
                        prevGradientData = gd_out.Clone();
                        //ctx = newCtx;
                        stepsInfo.Add(new StepInfo(prevGradientData, WCT, GOR, L, delta_p));
                    }

                    delta_p = dPsign * (K1 + 2 * K2 + 2 * K3 + K4) / 6 * delta_l;

                    L = L + delta_l * dLsign;
                    processedLen += delta_l;

                    var Pnext = P + delta_p;
                    if (U.isLE(Pnext, 0.0) || Pnext > maxGradientPressure)
                    {
#if DEBUG
                        //_logger.Debug(string.Format("P: P={0}, maxGradientPressure={1}", Pnext, maxGradientPressure));
#endif
                        break;
                    }

                    P = Pnext;
                    //var t = getTempK(q_osc, q_wsc, L);
                    //if (t < 0.0)
                    //{
                    //    //ShowTemperatureWarning(t_pvt);
                    //    t = 0;
                    //}
                    //Debug.Assert(!double.IsInfinity(t) && !double.IsNaN(t));

                    //gradientData.rho_l_avg += gradientData.rho_l * delta_l;
                    //gradientData.rho_g_avg += gradientData.rho_g * delta_l;
                    //gradientData.rho_n_avg += gradientData.rho_n * delta_l;
                    //gradientData.T = t.get();

                    //stepsInfo.Add(new GradientInfo(_fluidSim, WCT, GOR, L, P, t.get(), delta_p, 0, 0.0));
                }
                else
                {
                    //if (stepsInfo != null)
                    //    stepsInfo.Add(new StepInfo(ctx, prevGradientData, WCT, GOR, L, P));
                    if (double.IsNaN(err))
                        return double.NaN;
                    else
                        index--;
                }

                if (err > tolerance)
                    delta_l /= 2.0;
                else if (err / 100 < tolerance)
                    delta_l *= 2.0;
                delta_l = Math.Max(minStep, Math.Min(maxStep, delta_l));

                n_nodes = (int)((length - processedLen) / delta_l) + index + 1;
                last_delta_l = 0;
                if ((length - processedLen) > (n_nodes - index - 1) * delta_l)
                {
                    last_delta_l = length - processedLen - (n_nodes - index - 1) * delta_l;
                    n_nodes += 1;
                }
            }
            if (nIterations > 0)
                nIterations = -1;
            //stepsInfo.Back().copyFrom(gradientData);
            //gradientData.rho_n_avg /= length;
            //gradientData.rho_l_avg /= length;
            //gradientData.rho_g_avg /= length;

            if (stepsInfo != null)
                stepsInfo.Add(new StepInfo(prevGradientData, WCT, GOR, L, delta_p));

            return P;
        }
    }
}
