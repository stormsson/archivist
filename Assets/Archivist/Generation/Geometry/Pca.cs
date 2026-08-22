using System;
using System.Collections.Generic;
using Archivist.Generation.Determinism;

namespace Archivist.Generation.Geometry
{
    /// <summary>
    /// Outcome of a 2x2 principal-axis fit (§10.1 / D2). The fallback angle is the caller's
    /// business — Hydrographic falls back to 0.0°, Land Survey to theta_hydro + 90° — so this
    /// only ever reports that the fit is degenerate.
    /// </summary>
    public readonly struct PcaResult
    {
        /// <summary>Principal axis in degrees, in [0, 180), quantised to 0.1° (§4.4). 0.0 when degenerate.</summary>
        public readonly double AngleDeg;

        /// <summary>Larger eigenvalue of the covariance.</summary>
        public readonly double Lambda1;

        /// <summary>Smaller eigenvalue of the covariance.</summary>
        public readonly double Lambda2;

        /// <summary>Too few points, or Lambda2 &lt;= 0, or Lambda1/Lambda2 below the isotropy threshold.</summary>
        public readonly bool Degenerate;

        public PcaResult(double angleDeg, double lambda1, double lambda2, bool degenerate)
        {
            AngleDeg = angleDeg;
            Lambda1 = lambda1;
            Lambda2 = lambda2;
            Degenerate = degenerate;
        }

        /// <summary>Lambda1/Lambda2, or +infinity when Lambda2 is not positive. Reported by §13.7.</summary>
        public double Isotropy
        {
            get { return Lambda2 > 0.0 ? Lambda1 / Lambda2 : double.PositiveInfinity; }
        }

        public override string ToString()
        {
            return (Degenerate ? "degenerate " : "") + AngleDeg.ToString("F1") + "deg"
                 + " (l1=" + Lambda1.ToString("G4") + ", l2=" + Lambda2.ToString("G4") + ")";
        }
    }

    /// <summary>
    /// §10.1: rotation is derived, not rolled. One 2x2 covariance, closed-form eigen solution,
    /// no iteration and no external library.
    /// </summary>
    public static class Pca
    {
        /// <summary>
        /// Principal axis of <paramref name="points"/> (§10.1 / D2).
        ///
        /// Two-pass covariance about the mean, closed-form eigenvalues of the symmetric 2x2, then
        /// the eigenvector of the larger eigenvalue. atan2 is a transcendental, so the result is
        /// rounded to 0.1° via <see cref="Q.Deg"/> before anyone branches on it (§4.4).
        ///
        /// The angle is normalised into [0, 180): a principal axis has no direction, and reporting
        /// theta and theta+180 as different answers would make the isotropy guard meaningless.
        ///
        /// Callers must sample by arc length, not by vertex — marching squares emits vertices at a
        /// density that varies with how the line meets the lattice (§10.1).
        /// </summary>
        /// <param name="points">Sample points, in a caller-determined deterministic order.</param>
        /// <param name="isotropyThreshold">Lambda1/Lambda2 below this is degenerate (Tuning.PcaIsotropyThreshold).</param>
        /// <param name="minPoints">Fewer than this many points is degenerate (Tuning.PcaLandMinPoints).</param>
        public static PcaResult PrincipalAxis(IReadOnlyList<V2> points, double isotropyThreshold, int minPoints)
        {
            int n = points == null ? 0 : points.Count;
            if (n == 0) return new PcaResult(0.0, 0.0, 0.0, true);

            double sumX = 0.0, sumY = 0.0;
            for (int i = 0; i < n; i++)
            {
                V2 p = points[i];
                sumX += p.X;
                sumY += p.Y;
            }
            double meanX = sumX / n;
            double meanY = sumY / n;

            // Two-pass: subtract the mean before accumulating, rather than E[x^2] - E[x]^2, which
            // loses most of its significant digits on ground coordinates in the thousands of metres.
            double sxx = 0.0, syy = 0.0, sxy = 0.0;
            for (int i = 0; i < n; i++)
            {
                double dx = points[i].X - meanX;
                double dy = points[i].Y - meanY;
                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;
            }
            sxx /= n;
            syy /= n;
            sxy /= n;

            // Eigenvalues of [[sxx, sxy], [sxy, syy]]. Only + - * / sqrt, all IEEE-exact (§4.4).
            double half = (sxx + syy) * 0.5;
            double diff = (sxx - syy) * 0.5;
            double disc = Math.Sqrt(diff * diff + sxy * sxy);
            double lambda1 = half + disc;
            double lambda2 = half - disc;
            if (lambda2 < 0.0) lambda2 = 0.0;          // symmetric PSD; only rounding can go under

            bool degenerate = n < minPoints
                           || lambda2 <= 0.0
                           || lambda1 / lambda2 < isotropyThreshold;
            if (degenerate) return new PcaResult(0.0, lambda1, lambda2, true);

            // Eigenvector of lambda1. (lambda1 - syy, sxy) is the robust choice while sxy != 0;
            // sxy == 0 means the covariance is already diagonal and the axis is an axis of the frame.
            double vx, vy;
            if (sxy != 0.0)
            {
                vx = lambda1 - syy;
                vy = sxy;
            }
            else if (sxx >= syy)
            {
                vx = 1.0;
                vy = 0.0;
            }
            else
            {
                vx = 0.0;
                vy = 1.0;
            }

            double deg = Math.Atan2(vy, vx) * (180.0 / Math.PI);
            deg = Normalise180(deg);
            deg = Q.Deg(deg);
            deg = Normalise180(deg);                   // rounding 179.97 up must not land on 180.0

            return new PcaResult(deg, lambda1, lambda2, false);
        }

        /// <summary>Fold an angle into [0, 180) — a principal axis has no direction.</summary>
        static double Normalise180(double deg)
        {
            double d = deg % 180.0;
            if (d < 0.0) d += 180.0;
            if (d >= 180.0) d -= 180.0;
            return d;
        }
    }
}
