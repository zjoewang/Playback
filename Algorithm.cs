﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Playback
{
    public class Algorithm30102
    {
        public const int FS = 100;
        public const int BUFFER_SIZE = FS * 5;
        const int HR_FIFO_SIZE  = 7;
        const int MA4_SIZE = 4;     // DO NOT CHANGE
        const int HAMMING_SIZE = 5;     // DO NOT CHANGE

        // Hamm = long16(512* hamming(5)');
        int[] auw_hamm = new int[31] { 41, 276, 512, 276, 41, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        // SPO2table is computed as  -45.060*ratioAverage* ratioAverage + 30.354 *ratioAverage + 94.845
        byte[] uch_spo2_table = new byte[184] { 95, 95, 95, 96, 96, 96, 97, 97, 97, 97, 97, 98, 98, 98, 98, 98, 99, 99, 99, 99,
            99, 99, 99, 99, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100,
            100, 100, 100, 100, 99, 99, 99, 99, 99, 99, 99, 99, 98, 98, 98, 98, 98, 98, 97, 97,
            97, 97, 96, 96, 96, 96, 95, 95, 95, 94, 94, 94, 93, 93, 93, 92, 92, 92, 91, 91,
            90, 90, 89, 89, 89, 88, 88, 87, 87, 86, 86, 85, 85, 84, 84, 83, 82, 82, 81, 81,
            80, 80, 79, 78, 78, 77, 76, 76, 75, 74, 74, 73, 72, 72, 71, 70, 69, 69, 68, 67,
            66, 66, 65, 64, 63, 62, 62, 61, 60, 59, 58, 57, 56, 56, 55, 54, 53, 52, 51, 50,
            49, 48, 47, 46, 45, 44, 43, 42, 41, 40, 39, 38, 37, 36, 35, 34, 33, 31, 30, 29,
            28, 27, 26, 25, 23, 22, 21, 20, 19, 17, 16, 15, 14, 12, 11, 10, 9, 7, 6, 5,
            3, 2, 1, 0 };

        int[] an_dx = new int[BUFFER_SIZE - MA4_SIZE];
        int[] an_x = new int[BUFFER_SIZE];      //ir
        int[] an_y = new int [BUFFER_SIZE];     //red

        /**
         * \brief        Calculate the heart rate and SpO2 level
         * \par          Details
         *               By detecting  peaks of PPG cycle and corresponding AC/DC of red/infra-red signal, the ratio for the SPO2 is computed.
         *               Since this algorithm is aiming for Arm M0/M3. formaula for SPO2 did not achieve the accuracy due to register overflow.
         *               Thus, accurate SPO2 is precalculated and save longo uch_spo2_table[] per each ratio.
         *
         * \param[in]    *pun_ir_buffer           - IR sensor data buffer
         * \param[in]    n_ir_buffer_length      - IR sensor data buffer length
         * \param[in]    *pun_red_buffer          - Red sensor data buffer
         * \param[out]    *pn_spo2                - Calculated SpO2 value
         * \param[out]    *pch_spo2_valid         - 1 if the calculated SpO2 value is valid
         * \param[out]    *pn_heart_rate          - Calculated heart rate value
         * \param[out]    *pch_hr_valid           - 1 if the calculated heart rate value is valid
         *
         * \retval       None
         */
        public void maxim_heart_rate_and_oxygen_saturation(IEnumerable<int> pun_ir_buffer_IEnum, int n_ir_buffer_length, IEnumerable<int> pun_red_buffer_IEnum, out int pn_spo2, out bool pch_spo2_valid,
                out int pn_heart_rate, out bool pch_hr_valid)
        {
            int[] pun_ir_buffer = pun_ir_buffer_IEnum.ToArray();
            int[] pun_red_buffer = pun_red_buffer_IEnum.ToArray();

            int un_ir_mean, un_only_once;
            int k, n_i_ratio_count;
            int i, s, m, n_exact_ir_valley_locs_count, n_middle_idx;
            int n_th1, n_npks, n_c_min;
            int[] an_ir_valley_locs = new int[15];
            int[] an_exact_ir_valley_locs = new int[15];
            int[] an_dx_peak_locs = new int[15];
            int n_peak_interval_sum;
            int n_y_ac, n_x_ac;
            int n_spo2_calc;
            int n_y_dc_max, n_x_dc_max;
            int n_y_dc_max_idx = -1, n_x_dc_max_idx = -1;
            int[] an_ratio = new int[5];
            int n_ratio_average, n_nume, n_denom;

            Debug.Assert(pun_red_buffer.Length <= pun_ir_buffer.Length);
            Debug.Assert(n_ir_buffer_length <= pun_ir_buffer.Length);

            // Remove DC of ir signal    
            un_ir_mean = 0;

            for (k = 0; k < n_ir_buffer_length; k++)
                un_ir_mean += pun_ir_buffer[k];

            un_ir_mean = un_ir_mean / n_ir_buffer_length;

            for (k = 0; k < n_ir_buffer_length; k++)
                an_x[k] = pun_ir_buffer[k] - un_ir_mean;

            // 4 pt Moving Average
            for (k = 0; k < n_ir_buffer_length - MA4_SIZE; k++)
            {
                n_denom = (an_x[k] + an_x[k + 1] + an_x[k + 2] + an_x[k + 3]);
                an_x[k] = n_denom / (int)4;
            }

            // Get difference of smoothed IR signal
            for (k = 0; k < n_ir_buffer_length - MA4_SIZE - 1; k++)
                an_dx[k] = (an_x[k + 1] - an_x[k]);

            // 2-pt Moving Average to an_dx
            for (k = 0; k < n_ir_buffer_length - MA4_SIZE - 2; k++)
                an_dx[k] = (an_dx[k] + an_dx[k + 1]) / 2;

            // Hamming window: flip wave form so that we can detect valley with peak detector
            for (i = 0; i < n_ir_buffer_length - HAMMING_SIZE - MA4_SIZE - 2; i++)
            {
                s = 0;

                for (k = i; k < i + HAMMING_SIZE; k++)
                    s -= an_dx[k] * auw_hamm[k - i];

                an_dx[i] = s / (int)1146; // divide by sum of auw_hamm 
            }

            n_th1 = 0; // threshold calculation

            for (k = 0; k < n_ir_buffer_length - HAMMING_SIZE; k++)
                n_th1 += ((an_dx[k] > 0) ? an_dx[k] : ((int)0 - an_dx[k]));

            n_th1 = n_th1 / (n_ir_buffer_length - HAMMING_SIZE);

            // Peak location is acutally index for sharpest location of raw signal since we flipped the signal         
            maxim_find_peaks(an_dx_peak_locs, out n_npks, an_dx, n_ir_buffer_length - HAMMING_SIZE, n_th1, 8, 5);//peak_height, peak_distance, max_num_peaks 

            n_peak_interval_sum = 0;

            if (n_npks >= 2)
            {
                for (k = 1; k < n_npks; k++)
                    n_peak_interval_sum += (an_dx_peak_locs[k] - an_dx_peak_locs[k - 1]);

                n_peak_interval_sum = n_peak_interval_sum / (n_npks - 1);

                // Each data point represent 1/FS second in time so peak internal in seconds is
                // n_peak_interval_sum * (1 / FS).
                // The heart rate is 60 / peak interval = 60 * FS / n_peak_interal_sum
                pn_heart_rate = (int)(60 * FS / n_peak_interval_sum);      // Beats per minutes
                pch_hr_valid = true;
            }
            else
            {
                pn_heart_rate = -999;
                pch_hr_valid = false;
            }

            for (k = 0; k < n_npks; k++)
                an_ir_valley_locs[k] = an_dx_peak_locs[k] + HAMMING_SIZE / 2;

            // Raw value : RED(=y) and IR(=X)
            // We need to assess DC and AC value of ir and red PPG. 
            for (k = 0; k < n_ir_buffer_length; k++)
            {
                an_x[k] = pun_ir_buffer[k];
                an_y[k] = pun_red_buffer[k];
            }

            // Find precise min near an_ir_valley_locs
            n_exact_ir_valley_locs_count = 0;

            for (k = 0; k < n_npks; k++)
            {
                un_only_once = 1;
                m = an_ir_valley_locs[k];
                n_c_min = 16777216;     //2^24;

                if (m + 5 < n_ir_buffer_length - HAMMING_SIZE && m - 5 > 0)
                {
                    for (i = m - 5; i < m + 5; i++)
                    {
                        if (an_x[i] < n_c_min)
                        {
                            if (un_only_once > 0)
                            {
                                un_only_once = 0;
                            }
                            n_c_min = an_x[i];
                            an_exact_ir_valley_locs[k] = i;
                        }
                    }

                    if (un_only_once == 0)
                        n_exact_ir_valley_locs_count++;
                }
            }

            if (n_exact_ir_valley_locs_count < 2)
            {
                pn_spo2 = -999; // do not use SPO2 since signal ratio is out of range
                pch_spo2_valid = false;

                return;
            }

            // 4 pt MA
            for (k = 0; k < n_ir_buffer_length - MA4_SIZE; k++)
            {
                an_x[k] = (an_x[k] + an_x[k + 1] + an_x[k + 2] + an_x[k + 3]) / (int)4;
                an_y[k] = (an_y[k] + an_y[k + 1] + an_y[k + 2] + an_y[k + 3]) / (int)4;
            }

            // Using an_exact_ir_valley_locs , find ir-red DC andir-red AC for SPO2 calibration ratio
            // Finding AC/DC maximum of raw ir * red between two valley locations
            n_ratio_average = 0;
            n_i_ratio_count = 0;

            for (k = 0; k < 5; k++)
                an_ratio[k] = 0;

            for (k = 0; k < n_exact_ir_valley_locs_count; k++)
            {
                if (an_exact_ir_valley_locs[k] > n_ir_buffer_length)
                {
                    pn_spo2 = -999; // do not use SPO2 since valley loc is out of range
                    pch_spo2_valid = false;

                    return;
                }
            }

            // Find max between two valley locations 
            // and use ratio betwen AC compoent of Ir & Red and DC compoent of Ir & Red for SPO2 
            for (k = 0; k < n_exact_ir_valley_locs_count - 1; k++)
            {
                n_y_dc_max = -16777216;
                n_x_dc_max = -16777216;

                if (an_exact_ir_valley_locs[k + 1] - an_exact_ir_valley_locs[k] > 10)
                {
                    for (i = an_exact_ir_valley_locs[k]; i < an_exact_ir_valley_locs[k + 1]; i++)
                    {
                        if (an_x[i] > n_x_dc_max) { n_x_dc_max = an_x[i]; n_x_dc_max_idx = i; }
                        if (an_y[i] > n_y_dc_max) { n_y_dc_max = an_y[i]; n_y_dc_max_idx = i; }
                    }

                    n_y_ac = (an_y[an_exact_ir_valley_locs[k + 1]] - an_y[an_exact_ir_valley_locs[k]]) * (n_y_dc_max_idx - an_exact_ir_valley_locs[k]); //red
                    n_y_ac = an_y[an_exact_ir_valley_locs[k]] + n_y_ac / (an_exact_ir_valley_locs[k + 1] - an_exact_ir_valley_locs[k]);

                    n_y_ac = an_y[n_y_dc_max_idx] - n_y_ac;    // subracting linear DC compoenents from raw 
                    n_x_ac = (an_x[an_exact_ir_valley_locs[k + 1]] - an_x[an_exact_ir_valley_locs[k]]) * (n_x_dc_max_idx - an_exact_ir_valley_locs[k]); // ir
                    n_x_ac = an_x[an_exact_ir_valley_locs[k]] + n_x_ac / (an_exact_ir_valley_locs[k + 1] - an_exact_ir_valley_locs[k]);
                    n_x_ac = an_x[n_y_dc_max_idx] - n_x_ac;      // subracting linear DC compoenents from raw 
                    n_nume = (n_y_ac * n_x_dc_max) >> 7; //prepare X100 to preserve floating value
                    n_denom = (n_x_ac * n_y_dc_max) >> 7;

                    if (n_denom > 0 && n_i_ratio_count < 5 && n_nume != 0)
                    {
                        an_ratio[n_i_ratio_count] = (n_nume * 100) / n_denom; //formular is ( n_y_ac *n_x_dc_max) / ( n_x_ac *n_y_dc_max) ;
                        n_i_ratio_count++;
                    }
                }
            }

            maxim_sort_ascend(an_ratio, n_i_ratio_count);
            n_middle_idx = n_i_ratio_count / 2;

            if (n_middle_idx > 1)
                n_ratio_average = (an_ratio[n_middle_idx - 1] + an_ratio[n_middle_idx]) / 2; // use median
            else
                n_ratio_average = an_ratio[n_middle_idx];

            if (n_ratio_average > 2 && n_ratio_average < 184)
            {
                n_spo2_calc = uch_spo2_table[n_ratio_average];
                pn_spo2 = n_spo2_calc;
                pch_spo2_valid = true;//  float_SPO2 =  -45.060*n_ratio_average* n_ratio_average/10000 + 30.354 *n_ratio_average/100 + 94.845 ;  // for comparison with table
            }
            else
            {
                pn_spo2 = -999; // do not use SPO2 since signal ratio is out of range
                pch_spo2_valid = false;
            }
        }

        /**
         * \brief        Find peaks
         * \par          Details
         *               Find at most MAX_NUM peaks above MIN_HEIGHT separated by at least MIN_DISTANCE
         *
         * \retval       None
         */
        void maxim_find_peaks(int[] pn_locs, out int pn_npks, int[] pn_x, int n_size, int n_min_height, int n_min_distance, int n_max_num)
        {
            maxim_peaks_above_min_height(pn_locs, out pn_npks, pn_x, n_size, n_min_height);
            maxim_remove_close_peaks(pn_locs, pn_npks, pn_x, n_min_distance);
            pn_npks = Math.Min(pn_npks, n_max_num);
        }

        /**
         * \brief        Find peaks above n_min_height
         * \par          Details
         *               Find all peaks above MIN_HEIGHT
         *
         * \retval       None
         */
        void maxim_peaks_above_min_height(int[] pn_locs, out int pn_npks, int[] pn_x, int n_size, int n_min_height)
        {
            int i = 1, n_width;
            pn_npks = 0;

            while (i < n_size - 1)
            {
                if (pn_x[i] > n_min_height && pn_x[i] > pn_x[i - 1])
                {            // find left edge of potential peaks
                    n_width = 1;

                    while (i + n_width < n_size && pn_x[i] == pn_x[i + n_width])    // find flat peaks
                        n_width++;

                    if (pn_x[i] > pn_x[i + n_width] && pn_npks < 15)
                    {
                        // Find right edge of peaks
                        pn_locs[pn_npks++] = i;

                        // For flat peaks, peak location is left edge
                        i += n_width + 1;
                    }
                    else
                        i += n_width;
                }
                else
                    i++;
            }
        }

        /**
         * \brief        Remove peaks
         * \par          Details
         *               Remove peaks separated by less than MIN_DISTANCE
         *
         * \retval       None
         */
        void maxim_remove_close_peaks(int[] pn_locs, int pn_npks, int[] pn_x, int n_min_distance)
        {
            int i, j, n_old_npks, n_dist;

            // Order peaks from large to small
            maxim_sort_indices_descend(pn_x, pn_locs, pn_npks);

            for (i = -1; i < pn_npks; i++)
            {
                n_old_npks = pn_npks;
                pn_npks = i + 1;

                for (j = i + 1; j < n_old_npks; j++)
                {
                    n_dist = pn_locs[j] - (i == -1 ? -1 : pn_locs[i]); // lag-zero peak of autocorr is at index -1

                    if (n_dist > n_min_distance || n_dist < -n_min_distance)
                        pn_locs[pn_npks++] = pn_locs[j];
                }
            }

            // Resort indices longo ascending order
            maxim_sort_ascend(pn_locs, pn_npks);
        }

        /**
         * \brief        Sort array
         * \par          Details
         *               Sort array in ascending order (insertion sort algorithm)
         *
         * \retval       None
         */
        void maxim_sort_ascend(int[] pn_x, int n_size)
        {
            int i, j, n_temp;

            for (i = 1; i < n_size; i++)
            {
                n_temp = pn_x[i];

                for (j = i; j > 0 && n_temp < pn_x[j - 1]; j--)
                    pn_x[j] = pn_x[j - 1];

                pn_x[j] = n_temp;
            }
        }

        /**
         * \brief        Sort indices
         * \par          Details
         *               Sort indices according to descending order (insertion sort algorithm)
         *
         * \retval       None
         */
        void maxim_sort_indices_descend(int[] pn_x, int[] pn_indx, int n_size)
        {
            int i, j, n_temp;

            for (i = 1; i < n_size; i++)
            {
                n_temp = pn_indx[i];

                for (j = i; j > 0 && pn_x[n_temp] > pn_x[pn_indx[j - 1]]; j--)
                    pn_indx[j] = pn_indx[j - 1];

                pn_indx[j] = n_temp;
            }
        }
    }
}
