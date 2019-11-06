using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PPM.Pipeline.Fact.ApiModels.Service
{
    [Table("HYDRAULIC_CALCULATION")]
    public class HydraulicCalculation
    {
        public HydraulicCalculation()
        {           
        }

        [Key]
        public Guid Id { get; set; }

        [Column("START_CALCULATION_TIME")]
        public DateTime StartCalculationTime { get; set; }

        [Column("STOP_CALCULATION_TIME")]
        public DateTime StopCalculationTime { get; set; }

        [Column("CALCULATION_STATUS_RD")]
        public Guid CalculationStatusRd { get; set; }

        [ForeignKey(nameof(CalculationStatusRd))]
        public Guid CalculationStatusRdNavigation { get; set; }

        [Column("PIPES_COUNT")]
        public int PipesCount { get; set; }//

        [Column("SEGMENTS_COUNT")]
        public int SegmentsCount { get; set; }

        [ConcurrencyCheck]
        [Column("CALCULATED_COUNT")]
        public int CalculatedCount { get; set; }

        [Column("ERRORS_COUNT")]
        public int ErrorsCount { get; set; }

        [Column("WITH_SHEDULER")]
        public bool WithSheduler { get; set; }

        [Column("INITIATOR")]
        public string Initiator { get; set; }        
    }
}